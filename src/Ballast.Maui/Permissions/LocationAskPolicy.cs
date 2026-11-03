// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Permissions;

/// <summary>What the app does when the person asks for location and location is not on for the app.</summary>
public enum LocationAsk
{
    /// <summary>Location is already allowed: nothing to ask, the map is reloaded so it can centre on the person.</summary>
    AlreadyAllowed,

    /// <summary>Show the platform's own permission prompt.</summary>
    SystemPrompt,

    /// <summary>Show the "Location is off" dialog that opens the app's settings.</summary>
    OpenSettings,
}

/// <summary>
/// Chooses between the system prompt and the settings dialog for a location request. Pure, so the decision is pinned by a test.
/// </summary>
/// <remarks>
/// Android answers the prompt question only from its own memory: after a refusal it shows the prompt again, and after a second refusal, or
/// "don't ask again", it shows nothing and answers at once. So the app asks the system while the system will show its prompt, and sends the
/// person to settings once it will not.
/// </remarks>
public static class LocationAskPolicy
{
    /// <summary>
    /// The decision, checked in this order.
    /// </summary>
    /// <param name="allowed">Location is allowed for the app now.</param>
    /// <param name="showRationale">The platform says the permission was refused before and may be asked again (one refusal so far).</param>
    /// <param name="requestedOnThisInstall">This install has shown the system prompt before. A restored install says no.</param>
    /// <returns>What to do.</returns>
    public static LocationAsk Decide(bool allowed, bool showRationale, bool requestedOnThisInstall)
    {
        // Allowed: nothing to ask. The reload that follows is what lets the map centre on the person.
        if (allowed)
            return LocationAsk.AlreadyAllowed;

        // One refusal so far: the platform shows its prompt again, so the person is asked in the usual way.
        if (showRationale)
            return LocationAsk.SystemPrompt;

        // Never asked on this install. This includes a restored install, whose grant the platform did not restore, so the system prompt is the
        // first thing the person sees here, not a settings dialog for a question never asked.
        if (!requestedOnThisInstall)
            return LocationAsk.SystemPrompt;

        // Asked before, refused twice or "don't ask again": the platform will not show the prompt again, so the only place left to allow it is
        // settings. On iOS any refusal lands here, because it reports no rationale.
        return LocationAsk.OpenSettings;
    }
}
