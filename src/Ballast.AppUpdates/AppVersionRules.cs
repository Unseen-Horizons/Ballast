// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Ballast.AppUpdates;

/// <summary>
/// The decisions behind the update notice, with no platform dependency so they can be tested. Builds are compared as integers
/// (the build number, the Android versionCode), never as display strings.
/// </summary>
public static class AppVersionRules
{
    /// <summary>How long a successful check stays fresh. A build below the minimum does not wait: see <see cref="ShouldCheck"/>.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Required wins over everything, a dismissal included. Otherwise a newer published build is Available, or AvailableDismissed when
    /// the rider already dismissed this build or a later one. Required only announces: see <see cref="BannerFor"/>.
    /// </summary>
    /// <param name="currentBuild">The installed build number.</param>
    /// <param name="info">The server's answer.</param>
    /// <param name="dismissedLatestBuild">The build number the rider dismissed, or null for none.</param>
    /// <returns>The decision.</returns>
    public static AppUpdateDecision Decide(int currentBuild, AppVersionResponse info, int? dismissedLatestBuild) =>
        Decide(currentBuild, info, null, dismissedLatestBuild);

    /// <summary>
    /// As above, with the store's own answer. <paramref name="info"/> is null when the server has no versions for the platform (no
    /// answer, "no minimum"); <paramref name="store"/> is null when the store cannot say (a sideload, a failed call, or a platform
    /// with no such flow).
    /// <list type="bullet">
    /// <item>Required comes only from the server's <c>MinimumBuild</c> and wins over everything, a dismissal included.</item>
    /// <item>When the store answered, it alone decides the banner: the server's <c>LatestBuild</c> is not consulted, so a staged
    /// rollout or a server value ahead of the store never offers an update the store does not have.</item>
    /// <item>When it did not, the server's <c>LatestBuild</c> decides.</item>
    /// </list>
    /// </summary>
    /// <param name="currentBuild">The installed build number.</param>
    /// <param name="info">The server's answer, or null.</param>
    /// <param name="store">The store's answer, or null.</param>
    /// <param name="dismissedBuild">The build number the rider dismissed, or null for none.</param>
    /// <returns>The decision.</returns>
    public static AppUpdateDecision Decide(int currentBuild, AppVersionResponse? info, StoreUpdate? store, int? dismissedBuild)
    {
        if (info is not null && currentBuild < info.MinimumBuild)
        {
            return AppUpdateDecision.UpdateRequired;
        }

        var announced = AnnouncedBuild(currentBuild, info, store);
        if (announced <= 0)
        {
            return AppUpdateDecision.UpToDate;
        }

        return dismissedBuild is { } dismissed && dismissed >= announced
            ? AppUpdateDecision.UpdateAvailableDismissed
            : AppUpdateDecision.UpdateAvailable;
    }

    /// <summary>
    /// The banner for a decision. Required is always shown (a stored dismissal never hides it, because <see cref="Decide(int, AppVersionResponse?, StoreUpdate?, int?)"/>
    /// answers Required before it looks at one). Available is the dismissable offer. A dismissed offer and an up-to-date build show nothing.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <returns>The banner.</returns>
    public static UpdateBannerKind BannerFor(AppUpdateDecision decision) => decision switch
    {
        AppUpdateDecision.UpdateRequired => UpdateBannerKind.Required,
        AppUpdateDecision.UpdateAvailable => UpdateBannerKind.Offer,
        _ => UpdateBannerKind.None,
    };

    /// <summary>Only the offer can be dismissed. The Required banner stays until the build is supported again.</summary>
    /// <param name="banner">The banner.</param>
    /// <returns>True only for <see cref="UpdateBannerKind.Offer"/>.</returns>
    public static bool CanDismiss(UpdateBannerKind banner) => banner == UpdateBannerKind.Offer;

    /// <summary>
    /// The build number the banner would announce, 0 when there is none to offer. It is what a dismissal is stored against: the store's
    /// number when the store answered, the server's <c>LatestBuild</c> when it could not. Both are build numbers of the same kind, so one
    /// stored value serves either.
    /// </summary>
    /// <param name="currentBuild">The installed build number.</param>
    /// <param name="info">The server's answer, or null.</param>
    /// <param name="store">The store's answer, or null.</param>
    /// <returns>The build number to announce, or 0.</returns>
    public static int AnnouncedBuild(int currentBuild, AppVersionResponse? info, StoreUpdate? store)
    {
        if (store is not null)
        {
            return store.Available && store.VersionCode > currentBuild ? store.VersionCode : 0;
        }

        return info is not null && currentBuild < info.LatestBuild ? info.LatestBuild : 0;
    }

    /// <summary>
    /// The updater to ask about a newer build for this install channel: only the store channel has a store that can say. Any other
    /// channel, and an unknown one (null), consults nothing, so the server's number for that channel decides.
    /// </summary>
    /// <param name="channel">The install channel of this install, or null when unknown.</param>
    /// <param name="updater">The platform's updater, or null when there is none.</param>
    /// <param name="storeChannel">The channel whose store can say. Default <see cref="InstallChannels.Play"/>.</param>
    /// <returns>The updater to ask, or null.</returns>
    public static IAppUpdater? StoreUpdaterFor(string? channel, IAppUpdater? updater, string storeChannel = InstallChannels.Play) =>
        channel == storeChannel ? updater : null;

    /// <summary>
    /// Asks the updater what its store says, within <paramref name="timeout"/>. Null when there is no updater, it cannot tell, the answer
    /// is unusable, it took too long or it threw: each of those but the first is logged, and none of them blocks the check (the server's
    /// rule then decides). A cancelled <paramref name="ct"/> is not swallowed.
    /// </summary>
    /// <param name="updater">The updater to ask, or null.</param>
    /// <param name="timeout">How long the store may take to answer.</param>
    /// <param name="logger">Where the reason is logged, or null for none.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The store's answer, or null when it cannot tell.</returns>
    public static async Task<StoreUpdate?> AskStoreAsync(IAppUpdater? updater, TimeSpan timeout, ILogger? logger = null, CancellationToken ct = default)
    {
        if (updater is null)
        {
            return null;
        }

        try
        {
            var answer = await updater.IsUpdateAvailableAsync().WaitAsync(timeout, ct);
            if (answer is { Available: true, VersionCode: <= 0 })
            {
                logger?.LogWarning("The store reports an update but no build number for it; using the server's rule.");
                return null;
            }

            return answer;
        }
        catch (TimeoutException)
        {
            logger?.LogWarning("The store did not answer within {Timeout}; using the server's rule.", timeout);
            return null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Asking the store failed; using the server's rule.");
            return null;
        }
    }

    /// <summary>
    /// True when there was no successful check yet, when <see cref="CheckInterval"/> has passed, or when the last answer was Required (a build
    /// below the minimum asks again on every start and resume, so the banner goes as soon as the minimum is lowered). A last check in the
    /// future (the clock moved) is due, not "fresh for a long time".
    /// </summary>
    /// <param name="lastCheckUtc">When the last successful check was made, in UTC, or null when there was none.</param>
    /// <param name="nowUtc">The current time, in UTC.</param>
    /// <param name="lastWasRequired">Whether the last answer was Required.</param>
    /// <returns>True when a check is due.</returns>
    public static bool ShouldCheck(DateTime? lastCheckUtc, DateTime nowUtc, bool lastWasRequired)
    {
        if (lastWasRequired || lastCheckUtc is not { } last)
        {
            return true;
        }

        if (last > nowUtc)
        {
            return true;
        }

        return nowUtc - last >= CheckInterval;
    }

    /// <summary>
    /// The configured message for exactly this UI language, or null. Never another language's text: a rider in one language is not shown
    /// an English sentence as if it were the notice.
    /// </summary>
    /// <param name="messages">The messages by language code, or null for none.</param>
    /// <param name="uiLanguage">The UI language's two-letter code, compared without case.</param>
    /// <returns>The trimmed message for that language, or null.</returns>
    public static string? PickMessage(IReadOnlyDictionary<string, string>? messages, string uiLanguage)
    {
        if (messages is null || string.IsNullOrWhiteSpace(uiLanguage))
        {
            return null;
        }

        foreach (var (language, text) in messages)
        {
            if (string.Equals(language, uiLanguage, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }
}
