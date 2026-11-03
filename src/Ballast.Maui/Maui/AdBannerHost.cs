// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Ads;

/// <summary>
/// Owns the app's single <see cref="AdBannerSlot"/>, created on first use, and moves it between pages. A native view has one parent at a time, so
/// it is detached from the old page before it is attached to the next, or Android throws "the specified child already has a parent". Must be used
/// from the UI thread.
/// </summary>
public sealed class AdBannerHost
{
    private readonly IAdsService _adsService;
    private readonly AdsOptions _options;
    private AdBannerSlot? _slot;

    /// <summary>Creates the host.</summary>
    /// <param name="adsService">The app's ad state.</param>
    /// <param name="options">The ads options, which the slot reads for its unit and divider colours.</param>
    public AdBannerHost(IAdsService adsService, AdsOptions options)
    {
        ArgumentNullException.ThrowIfNull(adsService);
        ArgumentNullException.ThrowIfNull(options);
        _adsService = adsService;
        _options = options;
    }

    /// <summary>Whether the ads can start. A host with ads off never shows a banner.</summary>
    public bool IsEnabled => _adsService.IsEnabled;

    /// <summary>
    /// Puts the banner in row <paramref name="row"/> of <paramref name="pageGrid"/>, leaving whatever page had it. A move is a removal and an addition: in
    /// MAUI neither disconnects the slot's handler, so the same native view is attached again and no new ad is requested. The slot also carries a manual
    /// disconnect policy, so a popped page cannot tear it down.
    /// </summary>
    /// <param name="pageGrid">The page's grid.</param>
    /// <param name="row">The row the banner takes.</param>
    public void MoveTo(Grid pageGrid, int row)
    {
        ArgumentNullException.ThrowIfNull(pageGrid);
        if (!_adsService.IsEnabled)
            return;

        var slot = _slot ??= new AdBannerSlot(_adsService, _options);
        if (ReferenceEquals(slot.Parent, pageGrid))
            return;

        Detach();
        pageGrid.Add(slot, 0, row);
    }

    /// <summary>Takes the banner off its page and keeps it alive for the next one (a page the banner is kept off has none).</summary>
    public void Detach()
    {
        if (_slot?.Parent is Layout oldParent)
            oldParent.Remove(_slot);
    }
}
