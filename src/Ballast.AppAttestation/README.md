# Ballast.AppAttestation

Play Integrity app sessions for an API. An install proves itself once: it asks Google Play for an integrity verdict bound to a
nonce the server issued, and the server checks the verdict and gives the install a signed session token. A gate then refuses
the API's data calls that carry no session, and a one-use web ticket carries the session into a web view as an HttpOnly
cookie. The library also holds the Google decoder, and the owner's own sideloaded builds are accepted through a list of
developer certificates.

Install:

```
dotnet add package Ballast.AppAttestation --prerelease
```

## Example

```csharp
using Ballast.AppAttestation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    // In an app this comes from user secrets or an App Service setting, never from code.
    ["AppSession:Key"] = "an-example-signing-key-of-at-least-32-bytes",
});

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

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var manager = scope.ServiceProvider.GetRequiredService<AppSessionManager>();
var nonce = await manager.IssueNonceAsync();
Console.WriteLine($"Nonce issued: {nonce.Nonce.Length} characters.");
```

This runs as written and prints `Nonce issued: 22 characters.` The app's own wiring adds a thin controller over
`AppSessionManager`, the authentication scheme, the gate and the rate limit. The usage guide, `docs/AppAttestation.md`, has
every option, every public entry point and every way it fails. The Google Cloud and Play Console steps are in
`docs/setup/play-integrity.md`.
