// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Auth;

/// <summary>
/// The decisions around Google sign-in that need no MAUI types, so they are tested without a device: when the button is offered,
/// and how a Credential Manager error ends the attempt. The message keys for the outcomes are the app's own and stay with the app.
/// </summary>
public static class GoogleSignInRules
{
    /// <summary>
    /// The text Credential Manager gives when the rider closes the sheet themselves. A <c>GetCredentialCancellationException</c>
    /// carries this text for a real dismissal, and other text (for example "[16] Reauthentication is needed...") when Google refused.
    /// The type alone is not enough.
    /// </summary>
    public const string UserCancelMessage = "cancelled by the user";

    /// <summary>
    /// The button is offered only when the platform has a chooser and a Web client id is configured, so it is never shown for a flow
    /// that cannot work.
    /// </summary>
    /// <param name="platformAvailable">True when the platform has a Google chooser.</param>
    /// <param name="webClientId">The Web client id, or null or blank when none is configured.</param>
    public static bool IsOffered(bool platformAvailable, string? webClientId) =>
        platformAvailable && !string.IsNullOrWhiteSpace(webClientId);

    /// <summary>
    /// How a Credential Manager error ends the attempt. <paramref name="type"/> is the exception's class name and <paramref name="message"/>
    /// its text. Only a cancellation that says the user cancelled is <see cref="GoogleSignInOutcome.Cancelled"/> (nothing is shown). A
    /// cancellation-typed error for any other reason is a failure, and the rider is told, because treating it as a dismissal hid a real
    /// error behind a sheet that simply closed. No credential on the device is <see cref="GoogleSignInOutcome.NoAccount"/>, and everything
    /// else is <see cref="GoogleSignInOutcome.Failed"/>.
    /// </summary>
    /// <param name="type">The error's class name.</param>
    /// <param name="message">The error's text, or null.</param>
    public static GoogleSignInOutcome OutcomeForError(string type, string? message)
    {
        if (type.EndsWith("GetCredentialCancellationException", StringComparison.Ordinal))
            return message is not null && message.Contains(UserCancelMessage, StringComparison.OrdinalIgnoreCase)
                ? GoogleSignInOutcome.Cancelled
                : GoogleSignInOutcome.Failed;

        if (type.EndsWith("NoCredentialException", StringComparison.Ordinal))
            return GoogleSignInOutcome.NoAccount;

        return GoogleSignInOutcome.Failed;
    }
}
