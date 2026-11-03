// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Auth;

namespace Ballast.Maui.Tests;

/// <summary>When the Google button is offered, and what each device outcome means. The messages each outcome shows stay with the app, which owns its resource keys.</summary>
public sealed class GoogleSignInRulesTests
{
    // ---- The button -------------------------------------------------------------------------

    [Theory]
    [InlineData(true, "abc.apps.googleusercontent.com", true)]
    [InlineData(false, "abc.apps.googleusercontent.com", false)] // Windows and others: no chooser
    [InlineData(true, "", false)]                                // no Web client id configured
    [InlineData(true, "   ", false)]
    [InlineData(true, null, false)]
    public void The_button_is_offered_only_with_a_chooser_and_a_client_id(bool platform, string? clientId, bool expected)
        => Assert.Equal(expected, GoogleSignInRules.IsOffered(platform, clientId));

    [Fact]
    public void The_unavailable_stub_never_offers_the_button()
        => Assert.False(new UnavailableGoogleSignIn().IsAvailable);

    // ---- Device outcomes --------------------------------------------------------------------

    [Theory]
    [InlineData("GetCredentialCancellationException", "activity is cancelled by the user.", GoogleSignInOutcome.Cancelled)]
    [InlineData("GetCredentialCancellationException", "Activity Is Cancelled By The User", GoogleSignInOutcome.Cancelled)]
    [InlineData("GetCredentialCancellationException", "[16] Reauthentication is needed to proceed", GoogleSignInOutcome.Failed)]
    [InlineData("GetCredentialCancellationException", "reauth failed", GoogleSignInOutcome.Failed)]
    [InlineData("GetCredentialCancellationException", "something else entirely", GoogleSignInOutcome.Failed)]
    [InlineData("GetCredentialCancellationException", "", GoogleSignInOutcome.Failed)]
    [InlineData("GetCredentialCancellationException", null, GoogleSignInOutcome.Failed)]
    [InlineData("NoCredentialException", "No credentials available", GoogleSignInOutcome.NoAccount)]
    [InlineData("NoCredentialException", null, GoogleSignInOutcome.NoAccount)]
    [InlineData("GetCredentialProviderConfigurationException", "activity is cancelled by the user.", GoogleSignInOutcome.Failed)]
    [InlineData("GetCredentialUnknownException", "boom", GoogleSignInOutcome.Failed)]
    [InlineData("no error", null, GoogleSignInOutcome.Failed)]
    public void A_credential_manager_error_is_a_cancel_only_when_the_user_cancelled(string type, string? message, GoogleSignInOutcome expected)
    {
        Assert.Equal(expected, GoogleSignInRules.OutcomeForError(type, message));
    }
}
