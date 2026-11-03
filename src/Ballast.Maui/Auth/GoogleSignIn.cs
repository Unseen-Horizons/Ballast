// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Auth;

/// <summary>What the device's Google sign-in ended with.</summary>
public enum GoogleSignInOutcome
{
    /// <summary>The rider closed the sheet. Nothing is shown.</summary>
    Cancelled,

    /// <summary>No Google account is on the device.</summary>
    NoAccount,

    /// <summary>The flow failed for another reason.</summary>
    Failed,

    /// <summary>The rider signed in and Google gave an ID token.</summary>
    Ok,
}

/// <summary>The result of a device sign-in: the outcome, and the ID token when there is one.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="IdToken">Google's ID token, set only for <see cref="GoogleSignInOutcome.Ok"/>.</param>
public sealed record GoogleSignInResult(GoogleSignInOutcome Outcome, string? IdToken = null)
{
    /// <summary>The rider closed the sheet.</summary>
    public static GoogleSignInResult Cancelled { get; } = new(GoogleSignInOutcome.Cancelled);

    /// <summary>No account is on the device.</summary>
    public static GoogleSignInResult NoAccount { get; } = new(GoogleSignInOutcome.NoAccount);

    /// <summary>The flow failed.</summary>
    public static GoogleSignInResult Failed { get; } = new(GoogleSignInOutcome.Failed);

    /// <summary>A signed-in result carrying Google's ID token.</summary>
    /// <param name="idToken">The ID token.</param>
    public static GoogleSignInResult Ok(string idToken) => new(GoogleSignInOutcome.Ok, idToken);
}

/// <summary>The device's Google sign-in. The server verifies the ID token it returns; the device never decides who the rider is.</summary>
public interface IGoogleSignIn
{
    /// <summary>True when the button may be offered: the platform has a chooser and the Web client id is configured.</summary>
    bool IsAvailable { get; }

    /// <summary>Shows the sign-in and returns what it ended with.</summary>
    Task<GoogleSignInResult> SignInAsync();
}

/// <summary>A sign-in for a platform with no Google chooser (Windows, and any build with no Web client id).</summary>
public sealed class UnavailableGoogleSignIn : IGoogleSignIn
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<GoogleSignInResult> SignInAsync() => Task.FromResult(GoogleSignInResult.Failed);
}
