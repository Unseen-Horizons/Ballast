// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using AndroidX.Core.Content;
using AndroidX.Credentials;

using Ballast.Maui.Diagnostics;

using Google.Android.Libraries.Identity.GoogleId;

namespace Ballast.Maui.Auth;

/// <summary>
/// Google sign-in through Android's Credential Manager: the "Sign in with Google" bottom sheet. It hands back Google's ID token, which
/// the server verifies (its audience is the Web client id). Nothing about the account is trusted from the device: the token is the proof.
/// Every error is logged with its exception type and message (a cancel included), never the token.
/// </summary>
public sealed class CredentialManagerGoogleSignIn : IGoogleSignIn
{
    private readonly GoogleSignInOptions _options;
    private readonly IClientLog _log;

    /// <summary>Creates the sign-in.</summary>
    /// <param name="options">The Web client id.</param>
    /// <param name="log">Where the outcome of each attempt is recorded.</param>
    public CredentialManagerGoogleSignIn(GoogleSignInOptions options, IClientLog log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _options = options;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsAvailable => GoogleSignInRules.IsOffered(platformAvailable: true, _options.WebClientId);

    /// <inheritdoc />
    public async Task<GoogleSignInResult> SignInAsync()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var clientId = _options.WebClientId;
        if (activity is null || clientId is null || !IsAvailable)
        {
            _log.Failure("GoogleSignIn", "No activity or no Web client id: cannot show the Google sheet.");
            return GoogleSignInResult.Failed;
        }

        try
        {
            var option = new GetSignInWithGoogleOption.Builder(clientId).Build();
            var request = new GetCredentialRequest.Builder().AddCredentialOption(option).Build();
            var callback = new Callback(_log);
            using var cancel = new global::Android.OS.CancellationSignal();
            CredentialManager.Create(activity).GetCredentialAsync(
                activity,
                request,
                cancel,
                ContextCompat.GetMainExecutor(activity) ?? throw new InvalidOperationException("No main executor."),
                callback);
            return await callback.Result.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Failure("GoogleSignIn", "Sign-in failed before Credential Manager answered", code: ex.GetType().Name, detail: ex.Message, ex: ex);
            return GoogleSignInResult.Failed;
        }
    }

    private sealed class Callback : Java.Lang.Object, ICredentialManagerCallback
    {
        private readonly IClientLog _log;

        public Callback(IClientLog log) => _log = log;

        public TaskCompletionSource<GoogleSignInResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnResult(Java.Lang.Object? result)
        {
            if (result is GetCredentialResponse { Credential: CustomCredential custom } &&
                custom.Type == GoogleIdTokenCredential.TypeGoogleIdTokenCredential)
            {
                var token = GoogleIdTokenCredential.CreateFrom(custom.Data).IdToken;
                Result.TrySetResult(string.IsNullOrWhiteSpace(token) ? GoogleSignInResult.Failed : GoogleSignInResult.Ok(token));
                return;
            }

            _log.Failure("GoogleSignIn", "Unexpected credential response", code: (result as GetCredentialResponse)?.Credential?.GetType().Name ?? "none");
            Result.TrySetResult(GoogleSignInResult.Failed);
        }

        public void OnError(Java.Lang.Object? error)
        {
            // The binding hands the Kotlin exception over as a plain Java object, so it is told apart by class name.
            var type = error?.Class?.SimpleName ?? "no error";

            // Java's toString on a Throwable is "<class>: <message>", so the message text is in it.
            var message = error?.ToString();

            // Every error is recorded, a cancel included: a "cancellation" that is really Google refusing looked like the rider closing the
            // sheet, and nothing said so.
            var outcome = GoogleSignInRules.OutcomeForError(type, message);
            _log.Failure("GoogleSignIn", $"Credential Manager error, outcome {outcome}", code: type, detail: message);
            Result.TrySetResult(outcome switch
            {
                GoogleSignInOutcome.Cancelled => GoogleSignInResult.Cancelled,
                GoogleSignInOutcome.NoAccount => GoogleSignInResult.NoAccount,
                _ => GoogleSignInResult.Failed,
            });
        }
    }
}
