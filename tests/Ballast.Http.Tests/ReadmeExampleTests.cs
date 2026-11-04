// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Http.Tests;

/// <summary>
/// Runs the example in src/Ballast.Http/README.md exactly as it is written there. The README's using lines are at the top of this
/// file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.Http/README.md.
        var services = new ServiceCollection();
        services.AddSafeHttpClient("feeds", o => o.UserAgent = "ExampleApp/1.0 (+https://example.test/app)");
        services.AddPoliteFetcher("pages", o =>
        {
            o.ProductToken = "ExampleBot";
            o.BotInfoUrl = "https://example.test/bot";
        });
        using var provider = services.BuildServiceProvider();

        // Refused before anything is sent: a loopback address is not public.
        HttpSafetyReason? reason = null;
        string? redacted = null;
        try
        {
            var follower = new RedirectFollower(provider.GetRequiredService<IHttpClientFactory>(), "feeds");
            using var response = await follower.SendAsync(new RedirectRequest(new Uri("https://127.0.0.1/feed.zip")));
        }
        catch (HttpSafetyException ex)
        {
            Console.WriteLine($"{ex.Reason} {ex.RedactedUrl}");
            reason = ex.Reason;
            redacted = ex.RedactedUrl;
        }

        // A URL for a log: the query is marked as redacted, and the user information is dropped.
        var logged = UrlForLog.Redact("https://user:secret@feeds.example.test/gtfs.zip?apikey=SECRET");
        Console.WriteLine(logged);

        Assert.Equal(HttpSafetyReason.NonPublicAddress, reason);
        Assert.Equal("https://127.0.0.1/feed.zip", redacted);
        Assert.Equal("https://feeds.example.test/gtfs.zip?[redacted]", logged);
    }

    [Fact]
    public async Task The_readme_pacer_example_runs_as_written()
    {
        // The example, copied from src/Ballast.Http/README.md. The second request waits the three-second gap.
        using var pacer = new HostPacer();
        var first = await pacer.RunAsync("notices.example.test", TimeSpan.FromSeconds(3), () => Task.FromResult("first"));
        var second = await pacer.RunAsync("notices.example.test", TimeSpan.FromSeconds(3), () => Task.FromResult("second"));

        Assert.Equal("first second", $"{first} {second}");
    }
}
