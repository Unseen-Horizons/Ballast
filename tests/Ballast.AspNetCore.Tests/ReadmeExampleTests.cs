// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AspNetCore;
using Ballast.Errors;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.AspNetCore.Tests;

/// <summary>
/// Runs the example in src/Ballast.AspNetCore/README.md exactly as it is written there. The README's using lines are at the top of
/// this file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.AspNetCore/README.md.
        // Registered once, at startup. A timeout from the database is answered 503 with a Retry-After of 30 seconds.
        var services = new ServiceCollection();
        services.AddProblemResponses(options =>
        {
            options.ExceptionMappings.Add(new ExceptionMapping(error => error is TimeoutException, 503, ErrorCodes.ServiceBusy)
            {
                Title = "The service is busy.",
                RetryAfterSeconds = 30,
            });
        });
        await using var provider = services.BuildServiceProvider();

        // A request as the pipeline holds it. The API's handler throws an AppException, and the answer is written from it.
        var http = new DefaultHttpContext { RequestServices = provider };
        http.TraceIdentifier = "00-4bf92f35-01";
        http.Request.Path = "/items/42";
        http.Response.Body = new MemoryStream();
        var error = AppException.NotFound(ErrorCodes.NotFound, "Item not found.", "No item has id 42.");
        await ProblemResponses.WriteAsync(http, Problem.From(error, ProblemResponses.TraceIdOf(http), http.Request.Path.Value));

        http.Response.Body.Position = 0;
        Console.WriteLine($"{http.Response.StatusCode} {new StreamReader(http.Response.Body).ReadToEnd()}");

        Assert.Equal(404, http.Response.StatusCode);
    }

    [Fact]
    public async Task The_readme_credential_example_runs_as_written()
    {
        // The example, copied from src/Ballast.AspNetCore/README.md.
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddLogging();
        services.AddSecretProtector("Example.Credentials");
        await using var provider = services.BuildServiceProvider();
        var protector = provider.GetRequiredService<SecretProtector>();

        // Stored encrypted: the database never holds the plain value.
        var stored = protector.Protect("bearer-abc-123")!;
        Assert.True(stored.StartsWith("enc:v1:", StringComparison.Ordinal) && !stored.Contains("bearer-abc-123"));
        Assert.Equal("bearer-abc-123", protector.Unprotect(stored));

        // A payload from another purpose does not open. The read goes on without the credential, and the warning names the source.
        var other = new SecretProtector(provider.GetRequiredService<IDataProtectionProvider>(), NullLogger<SecretProtector>.Instance,
            new SecretProtectorOptions { Purpose = "Example.Other" });
        var read = other.UnprotectForRead(stored, "Feed North", out var warning);
        Assert.Equal("True Feed North: its stored credential could not be decrypted, so the request was sent without it. Re-enter the credential on the source.",
            $"{read is null} {warning}");
    }
}
