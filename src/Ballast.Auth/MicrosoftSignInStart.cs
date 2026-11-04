// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Authentication;

namespace Ballast.Auth;

/// <summary>
/// What the start step decides. A refusal is a redirect to the sign-in page, and Entra is not asked. Otherwise the app challenges
/// <see cref="Scheme"/> with <see cref="Properties"/>.
/// </summary>
/// <param name="RedirectUrl">The sign-in page address to redirect to, or null when the challenge goes ahead.</param>
/// <param name="Properties">The challenge's properties: the callback route and the sign-in hint. Null when refused.</param>
/// <param name="Scheme">The authentication scheme to challenge, from <see cref="MicrosoftSignInOptions.Scheme"/>.</param>
public sealed record MicrosoftStartDecision(string? RedirectUrl, AuthenticationProperties? Properties, string Scheme);

/// <summary>
/// The start step of Microsoft staff sign-in as a decision. It reads the settings and the typed address, and decides. It writes nothing and
/// asks nothing of Microsoft. The library's route (<see cref="MicrosoftSignInEndpoints"/>) and an app's own controller both use it.
/// </summary>
public static class MicrosoftSignInStart
{
    /// <summary>Decides the start of a Microsoft sign-in.</summary>
    /// <param name="microsoft">The Microsoft settings.</param>
    /// <param name="loginHint">The address the person typed, or null. A value that is not an address is left out, and the challenge still goes ahead.</param>
    /// <param name="completePath">The app's complete route, which Entra's handshake returns to. It is the challenge's redirect URI.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="microsoft"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="completePath"/> is null, empty or blank.</exception>
    public static MicrosoftStartDecision Decide(MicrosoftSignInOptions microsoft, string? loginHint, string completePath)
    {
        ArgumentNullException.ThrowIfNull(microsoft);
        ArgumentException.ThrowIfNullOrWhiteSpace(completePath);

        if (!microsoft.IsEnabled)
        {
            return Refusal(microsoft, MicrosoftRefusal.NotConfigured);
        }

        var hint = (loginHint ?? string.Empty).Trim().ToLowerInvariant();

        // A hint that is not an address is left out. An address at another company domain is refused before Entra is asked.
        var isAddress = hint.Length is > 0 and <= 256 && hint.Contains('@');
        if (isAddress && !microsoft.IsCompanyAddress(hint))
        {
            return Refusal(microsoft, MicrosoftRefusal.NotCompanyAddress);
        }

        var properties = new AuthenticationProperties { RedirectUri = completePath };
        if (isAddress)
        {
            properties.Items[MicrosoftSignInEndpoints.LoginHintItem] = hint;
        }

        return new MicrosoftStartDecision(null, properties, microsoft.Scheme);
    }

    private static MicrosoftStartDecision Refusal(MicrosoftSignInOptions microsoft, string refusal) =>
        new(MicrosoftSignInRedirects.Error(microsoft.LoginPagePath, refusal), null, microsoft.Scheme);
}
