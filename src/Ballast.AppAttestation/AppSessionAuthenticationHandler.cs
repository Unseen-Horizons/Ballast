// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.AppAttestation;

/// <summary>
/// The app-session authentication scheme (<see cref="AppAttestationDefaults.SchemeName"/>): it reads the session from the header or
/// the cookie. Only the endpoints that name it (the web-ticket route) ask for it; the data endpoints are gated by
/// <see cref="AppSessionGateMiddleware"/>. It is a separate handler from the bearer one, and the bearer pipeline is not touched.
/// </summary>
public sealed class AppSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppSessionValidator _validator;
    private readonly AppAttestationSetup _setup;

    /// <summary>Creates the handler. The framework constructs it per request.</summary>
    /// <param name="options">The scheme's options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    /// <param name="validator">The session validator.</param>
    /// <param name="setup">The setup, which names the header and the cookie.</param>
    public AppSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppSessionValidator validator,
        AppAttestationSetup setup) : base(options, logger, encoder)
    {
        _validator = validator;
        _setup = setup;
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var info = AppSessionContext.Get(Context)?.Info;
        if (info is null)
        {
            var token = AppSessionContext.ReadToken(Request, _setup);
            if (token is null)
                return AuthenticateResult.NoResult();

            var validation = await _validator.ValidateAsync(token);
            if (validation.Session is null)
                return AuthenticateResult.Fail("The app session was not accepted.");

            info = validation.Session;
            AppSessionContext.Set(Context, new AppSessionFeature(info, token));
        }

        return AuthenticateResult.Success(new AuthenticationTicket(AppSessionValidator.ToPrincipal(info), AppAttestationDefaults.SchemeName));
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => AppSessionContext.WriteRequiredAsync(Context, _setup);
}
