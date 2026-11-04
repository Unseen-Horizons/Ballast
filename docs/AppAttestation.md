# Ballast.AppAttestation: usage guide

Ballast.AppAttestation is the server half of Play Integrity app sessions for an API. An install proves itself once: it asks
Google Play for an integrity verdict that is bound to a nonce the server issued, and the server checks the verdict and gives the
install a signed session token. A gate refuses the API's data calls that carry no session. A one-use web ticket carries the
session into a web view as an HttpOnly, Secure, SameSite=Strict cookie. The owner's own sideloaded builds are accepted through a
list of developer certificates, so a build from an IDE works on a real device.

The library targets ASP.NET Core 10. Its namespace is `Ballast.AppAttestation`. It has no MAUI types: the client half of the
protocol lives in the app. The package README has the install line and a runnable example. This guide covers every option, every
public entry point and every way each one fails.

The Google Cloud and Play Console steps are in [setup/play-integrity.md](setup/play-integrity.md).

## The flow

1. The install calls `GET` on the nonce route. The app's controller calls `AppSessionManager.IssueNonceAsync`, and gets a
   single-use nonce that answers for five minutes.
2. The install asks Play Integrity for a token, bound to that nonce, and posts it with its platform and install id to the session
   route. The app's controller calls `AppSessionManager.CreateSessionAsync`.
3. `CreateSessionAsync` spends the nonce first, then asks the verifier. Play recognises the app (attestation `play`), or the build
   is one of the owner's developer certificates (attestation `dev`). The session is an HS256 token, valid for 24 hours by default,
   and nothing about it is stored.
4. Each data call sends the session in the header, or in the cookie a web view carries. The gate validates it, parks it on the
   request, and lets the call through. A call with no valid session is refused with 401 once `Enforce` is on, and only logged
   before that.
5. A web view cannot send a header, so the install asks for a web ticket (`IssueWebTicketAsync`, a sixty-second single-use
   value) and opens the web view at the ticket route. `RedeemWebTicketAsync` spends the ticket, checks the redirect path, and
   `AppSessionCookie.Write` sets the cookie. The web view then carries the session on every call.

## Wiring it into an app

The library holds the rules, and the app keeps its controllers thin. The app's controller passes values in and writes the
answer. This is the shape the library's own tests use:

```csharp
[ApiController]
[Route("app/session")]
[EnableRateLimiting("AppSession")]
public sealed class AppSessionController : ControllerBase
{
    private readonly AppSessionManager _manager;
    private readonly AppAttestationSetup _setup;

    public AppSessionController(AppSessionManager manager, AppAttestationSetup setup)
    {
        _manager = manager;
        _setup = setup;
    }

    [HttpGet("nonce"), AllowAnonymous]
    public async Task<ActionResult<AppNonceResponse>> Nonce(CancellationToken ct) => Ok(await _manager.IssueNonceAsync(ct));

    [HttpPost, AllowAnonymous]
    public async Task<ActionResult<AppSessionResponse>> Create([FromBody] CreateAppSessionRequest request, CancellationToken ct) =>
        Ok(await _manager.CreateSessionAsync(request, ct));

    [HttpPost("web-ticket"), Authorize(AuthenticationSchemes = AppAttestationDefaults.SchemeName)]
    public async Task<ActionResult<AppWebTicketResponse>> WebTicket(CancellationToken ct) =>
        Ok(await _manager.IssueWebTicketAsync(AppSessionContext.Get(HttpContext)!, ct));

    [HttpGet("web"), AllowAnonymous]
    public async Task<IActionResult> Web([FromQuery] string? ticket, [FromQuery] string? next, CancellationToken ct)
    {
        var redemption = await _manager.RedeemWebTicketAsync(ticket, next, ct);
        AppSessionCookie.Write(Response, _setup, redemption);
        Response.Headers.CacheControl = "no-store";
        return LocalRedirect(redemption.RedirectTo);
    }
}
```

The data endpoints carry `[RequireAppSession]` and `[AllowAnonymous]`, which makes the gate the access rule. The app's
`Program.cs` does the rest, in this order:

```csharp
builder.Services.AddProblemResponses();
builder.Services.AddAuthentication().AddAppAttestationScheme();
builder.Services.AddAppAttestation(builder.Configuration, builder.Environment, setup =>
{
    setup.PackageName = "com.example.app";
    setup.Issuer = "example-api";
    setup.Audience = "example-app-session";
    setup.HeaderName = "X-App-Session";
    setup.CookieName = "example_app";
    setup.RedirectPrefix = "/view/";
    setup.SessionRequiredDetail = "This endpoint answers only the example app.";
    setup.SessionRequiredCode = AppAttestationCodes.SessionRequired;
    setup.AttestationFailedCode = AppAttestationCodes.AttestationFailed;
    setup.AttestationUnavailableCode = AppAttestationCodes.AttestationUnavailable;
});
builder.Services.AddRateLimits(builder.Configuration, options => options.AddAppAttestationPolicy("AppSession", 10));

app.UseProblemResponses();
app.UseAuthentication();
app.UseAppAttestationGate();   // after authentication, before the rate limiter
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
```

## Setup values: `AppAttestationSetup`

The values an app owns. None of them is read from configuration, and none has a library default except the three section names
and the title. A missing value stops the registration (see "How it fails").

| Property | Default | What it is |
|---|---|---|
| `PackageName` | none, required | The Android application id a verdict must name, for example `com.example.app`. The library never guesses one. |
| `Issuer` | none, required | The `iss` of a session token. |
| `Audience` | none, required | The `aud` of a session token. It must differ from the user tokens' audience, so a user token is never read as a session. |
| `HeaderName` | none, required | The request header a client sends its session in, for example `X-App-Session`. No spaces, colons or non-ASCII characters. |
| `CookieName` | none, required | The cookie a web view carries its session in. No spaces, semicolons, commas, equals signs or non-ASCII characters. |
| `RedirectPrefix` | none, required | The path prefix a web ticket may redirect to, for example `/view/`. It starts and ends with a slash, is printable ASCII, and holds no backslash. |
| `Platforms` | `android`, `ios`, `maccatalyst`, `windows` | The platform names a client may report, lower case. Any other is a 400. |
| `SessionRequiredTitle` | `App verification required` | The title of the 401 answer. |
| `SessionRequiredDetail` | none, required | The detail of the 401 answer, in the app's own words. |
| `SessionRequiredCode` | none, required | The code of the 401 answer. Use `AppAttestationCodes.SessionRequired`. |
| `AttestationFailedCode` | none, required | The code of the 403 answer. Use `AppAttestationCodes.AttestationFailed`. |
| `AttestationUnavailableCode` | none, required | The code of the 503 answer. Use `AppAttestationCodes.AttestationUnavailable`. |
| `SessionSection` | `AppSession` | The configuration section `AppSessionOptions` is bound from. |
| `AttestationSection` | `AppAttestation` | The configuration section `AppAttestationOptions` is bound from. |
| `PlayIntegritySection` | `PlayIntegrity` | The configuration section `PlayIntegrityOptions` is bound from. |

`Validate()` checks all of these and is called by the registration. It throws `InvalidOperationException` naming the property.

The codes are the wire clients are written against, so a released value is never changed. `AppAttestationCodes` holds the three
stable values, which are the ones the first consuming app shipped with.

`AppAttestationDefaults` holds the names no app chooses: the scheme name `AppSession`, the token claims `sid`, `att` and `plat`,
the placeholder `CHANGE-ME` and the 32-byte minimum key.

## Settings: the configuration sections

These are bound from configuration. The names are relative to the sections the setup gives (the defaults shown).

### `AppSessionOptions` (`AppSession`)

| Key | Default | Meaning |
|---|---|---|
| `Key` | none, required | The session signing key, at least 32 bytes in UTF-8. Set it in user secrets or an App Service setting, never in a file in the repository. Startup stops when it is missing, a `CHANGE-ME` placeholder, or too short. It is separate from the user-token key, so rotating one never signs out the other. |
| `LifetimeHours` | `24` | How long a session is valid, from 1 to 168 hours. A value outside that range stops the start. |
| `NonceLifetime` | `00:05:00` | How long a nonce answers. It must be longer than zero. |
| `TicketLifetime` | `00:01:00` | How long a web ticket answers. It must be longer than zero. |
| `StoreCapacity` | `20000` | How many nonces, and separately how many web tickets, the in-memory stores hold live at once. At least 1. |

`Lifetime` is `LifetimeHours` as a time span. The lifetime is validated when the host starts, so a bad value cannot wait for the
first request.

### `AppAttestationOptions` (`AppAttestation`)

| Key | Default | Meaning |
|---|---|---|
| `Enforce` | `false` | False: a data call with no valid session is let through and logged as "would be required (log-only)". True: it is refused with 401 and the setup's `SessionRequiredCode`. Turn it on only once no supported build can fail to attest. |
| `AllowUnattested` | `false` | Development only. It makes the session route issue an unattested `none` session with no Google call, so an emulator or a desktop run works. It is ignored in every other environment, and startup logs an error when it is set there. |
| `DeveloperCertificates` | empty | The SHA-256 digests of the owner's own signing keys (the debug key and the upload key), in hex as `keytool` prints it, or base64url as Play Integrity returns it. One value may hold several, separated by commas or semicolons. A build signed with one of them, on a genuine device, gets attestation `dev`. An entry that is not a SHA-256 digest is logged at startup and never matches. |

### `PlayIntegrityOptions` (`PlayIntegrity`)

| Key | Default | Meaning |
|---|---|---|
| `ServiceAccountJson` | empty | The Google service account's JSON key, as one string. Empty means no app can be attested: every attestation answers 503. Set it as a secret. |
| `ApplicationName` | `Ballast.AppAttestation` | The application name the Google client reports. It is a label, and it is not part of any verdict. |

## Registration: `AddAppAttestation`

`AddAppAttestation(configuration, environment, configure)` registers, in one call: the setup (as a singleton), the three
options, the validator, the token signer, the two stores (in memory, unless the app registered its own first), the Google
decoder, the verifier, the manager (scoped) and the startup check. It returns the services for chaining.

Call it once. Call `AddAppAttestationScheme()` on the authentication builder, `UseAppAttestationGate()` after
`UseAuthentication()`, and `AddAppAttestationPolicy(...)` on the rate limits.

`AddAppAttestationLimit(RateLimiterOptions, policyName, permitPerMinute)` is the same limit on ASP.NET's own rate limiter, for a
host that does not use `AddRateLimits`. A fixed window of one minute, partitioned by session, then user, then address.

`AddAppAttestationPolicy(RateLimitOptions, policyName, defaultPermitPerMinute, configurationKey)` adds a fixed-window policy
partitioned the same way. The policy name is the app's own argument, because an endpoint's `[EnableRateLimiting]` names it, and
the library holds no name for it. The limit is read from `{policyName}PerMinute` unless `configurationKey` names another key.

## Stores: `IAppSessionNonceStore` and `IAppSessionTicketStore`

The nonce and the ticket each sit behind an interface. The library's defaults are `InMemoryAppSessionNonceStore` and
`InMemoryAppSessionTicketStore`: bounded dictionaries in this process's memory, single use, expiring on the `TimeProvider`.

**The in-memory stores hold only within one process.** Two copies of the API during a deploy do not share them. A nonce issued
by one copy cannot be spent at the other, and a web ticket issued by one cannot be redeemed at the other. The effect is the same
answer an unknown nonce gets (403), or a web view that opens without its cookie. An app that runs more than one copy at once
needs a store that every copy shares. It registers its own implementation before `AddAppAttestation`, which keeps any store
already registered:

```csharp
builder.Services.AddSingleton<IAppSessionNonceStore, MySharedNonceStore>();
builder.Services.AddAppAttestation(...);
```

A store's `AddAsync` returns false when it is full. The library then answers 503 and never evicts a live entry. `TakeAsync`
returns true once, for a value that was stored and has not expired.

## The session token

`AppSessionTokenService.Issue` signs HS256 over `AppSession:Key`, with the setup's `Issuer` and `Audience`, and the claims
`sid` (a random id per session), `att` (`play`, `dev` or `none`) and `plat`. There is no user, no role and no refresh token. The
only way to end a session early is to change the key.

`AppSessionValidator` checks the signature, the issuer, the audience, the algorithm (HS256 only) and the lifetime, with 30
seconds of clock skew. A token with another audience, a user token, or a `none` session outside Development is not a session.

`AppSessionValidator.ToPrincipal` gives a session's principal. It carries no `sub`, so nothing reads it as a user.

## Verdicts: `PlayIntegrityVerifier`, `IPlayIntegrityDecoder`, `GooglePlayIntegrityDecoder`

`PlayIntegrityVerifier.VerifyAsync(token, expectedNonce, ct)` decodes the token through an `IPlayIntegrityDecoder` and returns an
`AttestationDecision`. It accepts when all of these hold:

- the verdict's request package and its app package are both the setup's `PackageName`;
- the verdict answers the nonce the server issued, compared as bytes (padded or not, URL-safe or standard base64);
- the device verdict includes `MEETS_DEVICE_INTEGRITY`;
- and either the app is `PLAY_RECOGNIZED` (`play`), or it is `UNRECOGNIZED_VERSION` and every certificate digest in the verdict is
  a listed developer certificate (`dev`).

Anything else is a refusal, logged with the verdict's own fields and never the token.

`GooglePlayIntegrityDecoder` calls `decodeIntegrityToken` as the service account in `PlayIntegrity:ServiceAccountJson`. It builds
its client on the first call and disposes it with the host. It never logs the service account.

## Entry points

| Type or member | What it does |
|---|---|
| `AppAttestationRegistration.AddAppAttestation` | Registers everything above. |
| `AppAttestationRegistration.AddAppAttestationScheme` | Adds the `AppSession` authentication scheme. |
| `AppAttestationRegistration.UseAppAttestationGate` | Adds the gate middleware. |
| `AppAttestationRegistration.AddAppAttestationPolicy` | Adds a rate-limit policy on `RateLimitOptions`. |
| `AppAttestationRegistration.AddAppAttestationLimit` | Adds the same limit on `RateLimiterOptions`. |
| `AppSessionManager.IssueNonceAsync` | Issues a nonce. |
| `AppSessionManager.CreateSessionAsync` | Attests the install and issues a session. |
| `AppSessionManager.IssueWebTicketAsync` | Issues a ticket for the caller's session. |
| `AppSessionManager.RedeemWebTicketAsync` | Spends a ticket and checks the redirect path. |
| `AppSessionManager.IsSafeRedirectPath` | The path rule, for a given prefix. |
| `AppSessionManager.MaxNextLength` | The longest redirect path accepted: 8192. |
| `AppSessionCookie.CreateOptions`, `AppSessionCookie.Write` | The cookie's options, and the cookie a redemption sets. |
| `AppSessionContext.ReadToken`, `Get`, `RateLimitKey`, `LogLabel`, `WriteRequiredAsync` | Reading the session off a request, the rate-limit partition, the log label of a partition, and the 401. |
| `AppSessionValidator` | Validates a token, and gives a session's principal. |
| `AppSessionTokenService.Issue` | Signs a session. |
| `AppSessionGateMiddleware`, `AppSessionAuthenticationHandler`, `WouldRefuseLog` | The gate, the scheme and the log of would-be refusals. |
| `AppAttestationStartupCheck` | Logs what the configuration does at start. |
| `PlayIntegrityVerifier`, `IPlayIntegrityDecoder`, `GooglePlayIntegrityDecoder` | The verdict decision, its decoder seam and Google's decoder. |
| `IAppSessionNonceStore`, `IAppSessionTicketStore` and the two in-memory stores | The store seams. |
| `RequireAppSessionAttribute` | The marker for a data endpoint. The name is part of the contract a surface test reads. |
| `CreateAppSessionRequest`, `AppNonceResponse`, `AppSessionResponse`, `AppWebTicketResponse` | The wire shapes. The JSON names are `platform`, `installId`, `nonce`, `integrityToken`, `accessToken`, `expiresInSeconds` and `ticket`. |
| `AppAttestationCodes`, `AppAttestationDefaults`, `Attestation` | The stable codes, the names the library keeps, and the three attestation values. |

## How it fails

### At startup (the host does not start)

- `AddAppAttestation` throws `InvalidOperationException` when a setup value is blank or unusable. The message names the property,
  for example `AppAttestationSetup.RedirectPrefix must start and end with a slash`.
- It throws `InvalidOperationException` when `AppSession:Key` is missing, is `CHANGE-ME`, or is shorter than 32 bytes. The message
  names the key and says how to set it, and never shows the value.
- The host fails to start, with `OptionsValidationException`, when `AppSession:LifetimeHours` is outside 1 to 168, or a
  `NonceLifetime` or `TicketLifetime` is not longer than zero, or `StoreCapacity` is below 1. A bad value is never silently replaced.

### Logged at startup (the host starts)

- `AppAttestation:AllowUnattested` set outside Development: an error is logged and the setting is ignored.
- A `DeveloperCertificates` entry that is not a SHA-256 digest: an error is logged, and that entry never matches.
- Outside Development with no `PlayIntegrity:ServiceAccountJson`: a warning is logged.
- Always: one line that says whether the gate is enforced or log-only, and how many developer certificates are configured.

### On a request

| Situation | Answer |
|---|---|
| Unknown platform, an install id outside 8 to 64 characters, a missing or over-long nonce | 400, `VALIDATION_FAILED`. Nothing is spent. |
| Nonce unknown, used or expired | 403, `AttestationFailedCode`. Google is not asked. |
| No integrity token, outside Development or with the bypass off | 403, `AttestationFailedCode`. |
| The verdict is refused (wrong package, another nonce, no device integrity, an unlisted sideload, an unevaluated app) | 403, `AttestationFailedCode`. The refusal is logged with the verdict's fields. |
| Google fails, is unreachable, returns no payload, or the service account is absent or unusable | 503, `AttestationUnavailableCode`. Never a session. |
| The nonce or ticket store is full | 503, `AttestationUnavailableCode`. Nothing is evicted. |
| A data call with no valid session, enforced | 401, `SessionRequiredCode`, with the setup's title and detail. |
| A data call with no valid session, log-only | Allowed. One line a minute per route and caller is logged. |
| An expired session presented on a data call | Treated as no session: 401 when enforced, a log line when not. |
| The ticket route with no session | 401 (the scheme's challenge). |
| `next` not under the prefix, absolute, protocol-relative, containing a backslash, a control character, a space, non-ASCII, or a `..` segment (encoded or not) | 400, `VALIDATION_FAILED`. The ticket is not spent. |
| A ticket that is unknown, used or expired | Redirect with no cookie. A warning is logged. The page loads and asks for a session if its data needs one. |
| A ticket whose session has ended by the time it is redeemed | Redirect with no cookie. |
| Too many requests on a policy | 429, `RATE_LIMITED` (from Ballast.AspNetCore). |

Every answer is `application/problem+json` and carries `code`, `traceId` and the `X-Trace-Id` header, as Ballast.AspNetCore writes them.

### Refusals are not substitutions

A Google failure is 503, never a pass, and the library has no offline path. A refused install is told only that it was refused. The
reason is logged, with the verdict's fields and never the token.

## What the library does not do

- It does not write the endpoints of an app. The thin controller is the app's, so the app's surface test sees every action.
- It does not know the app's user tokens, its roles or its sign-in. It never reads a user's key or audience.
- It does not store sessions. A session is stateless, so ending one early means changing the key.
- It does not talk to Google except through `GooglePlayIntegrityDecoder`, and only when a token is decoded.
- It does not run on the MAUI client. The client side of the protocol is a separate library part.

## Packaging

`Ballast.AppAttestation` is one package. It depends on Ballast.AspNetCore and Ballast.Errors, Google's Play Integrity client
(`Google.Apis.PlayIntegrity.v1`, Apache-2.0), `Google.Apis.Auth` and `Microsoft.IdentityModel.JsonWebTokens` (MIT). It is not
trim-safe or AOT-compatible, because ASP.NET Core and the Google client are not, and the project says so.
