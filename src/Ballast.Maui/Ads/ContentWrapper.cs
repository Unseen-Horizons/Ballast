// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Ads;

/// <summary>
/// The wrap-once step behind the ad banner's page decorator, free of MAUI types so a test can drive it.
/// </summary>
/// <remarks>
/// The wrap runs inside the page handler's content mapping, and every assignment to the page's content while it runs makes the handler
/// map the content again, which runs the same hook again. Each of the wrap's own assignments is therefore a re-entry, and an unguarded
/// wrap would wrap the wrapper, then wrap that, without end. Two things stop it: the state is recorded before the content is assigned
/// (<see cref="IsWrapped"/> is true by then), and a re-entered call returns at once while a wrap is in progress.
/// <para>One instance per page.</para>
/// </remarks>
/// <typeparam name="TContent">The type of the page's content.</typeparam>
public sealed class ContentWrapper<TContent> where TContent : class
{
    private bool _wrapping;

    /// <summary>True once the content has been wrapped. It is set before the wrapper is assigned as the page's content.</summary>
    public bool IsWrapped { get; private set; }

    /// <summary>
    /// Wraps the current content once. Returns true when the content is wrapped (now, earlier, or by the call that is still running), and
    /// false when there is no content yet.
    /// </summary>
    /// <param name="getContent">Reads the page's content.</param>
    /// <param name="setContent">Assigns the page's content. May re-enter this method.</param>
    /// <param name="wrap">
    /// Builds the wrapper around the original content and records whatever the page needs to find it again. Runs before the wrapper is
    /// assigned, so a re-entered mapping already sees it.
    /// </param>
    /// <returns>True when the content is wrapped, false when there is no content yet.</returns>
    public bool EnsureWrapped(Func<TContent?> getContent, Action<TContent?> setContent, Func<TContent, TContent> wrap)
    {
        ArgumentNullException.ThrowIfNull(getContent);
        ArgumentNullException.ThrowIfNull(setContent);
        ArgumentNullException.ThrowIfNull(wrap);

        if (IsWrapped || _wrapping)
            return true;
        if (getContent() is not { } original)
            return false;

        _wrapping = true;
        try
        {
            // Detached first: a view cannot be added to the wrapper while the page is still its parent.
            setContent(null);
            var wrapper = wrap(original);
            IsWrapped = true;
            setContent(wrapper);
            return true;
        }
        catch
        {
            // Never leave the page blank: give it its own content back and let the failure surface.
            if (!IsWrapped)
                setContent(original);
            throw;
        }
        finally
        {
            _wrapping = false;
        }
    }
}
