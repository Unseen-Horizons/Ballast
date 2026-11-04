// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AppAttestation;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ballast.AppAttestation.Tests;

/// <summary>
/// Runs the example in src/Ballast.AppAttestation/README.md exactly as it is written there. The README's using lines are at the top of
/// this file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.AppAttestation/README.md (its code block, from the first line to the last).
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

        Assert.Equal(22, nonce.Nonce.Length);
    }
}
