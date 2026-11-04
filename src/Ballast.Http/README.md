# Ballast.Http

Safe outbound HTTP for an app that fetches addresses it does not control. It refuses a request to a non-public address, follows
redirects by one rule set, reads a body under a size cap, keeps a per-host credential out of the wrong hands, writes a URL to a log
without its secrets, honours a server's Retry-After, and reads public pages politely (robots.txt, crawl delay, per-host pacing).
It depends only on Microsoft.Extensions packages, and it has no ASP.NET Core dependency.

Install:

```
dotnet add package Ballast.Http --prerelease
```

## Example

```csharp
using Ballast.Http;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSafeHttpClient("feeds", o => o.UserAgent = "ExampleApp/1.0 (+https://example.test/app)");
services.AddPoliteFetcher("pages", o =>
{
    o.ProductToken = "ExampleBot";
    o.BotInfoUrl = "https://example.test/bot";
});
using var provider = services.BuildServiceProvider();

// Refused before anything is sent: a loopback address is not public.
try
{
    var follower = new RedirectFollower(provider.GetRequiredService<IHttpClientFactory>(), "feeds");
    using var response = await follower.SendAsync(new RedirectRequest(new Uri("https://127.0.0.1/feed.zip")));
}
catch (HttpSafetyException ex)
{
    Console.WriteLine($"{ex.Reason} {ex.RedactedUrl}");
}

// A URL for a log: the query is marked as redacted, and the user information is dropped.
Console.WriteLine(UrlForLog.Redact("https://user:secret@feeds.example.test/gtfs.zip?apikey=SECRET"));
```

The example prints `NonPublicAddress https://127.0.0.1/feed.zip` and then `https://feeds.example.test/gtfs.zip?[redacted]`.

## Pacing requests to one host

```csharp
using Ballast.Http;

// One pacer for the application, registered as a singleton. The second request to a host starts 3 seconds after the first one ended.
using var pacer = new HostPacer();
var first = await pacer.RunAsync("notices.example.test", TimeSpan.FromSeconds(3), () => Task.FromResult("first"));
var second = await pacer.RunAsync("notices.example.test", TimeSpan.FromSeconds(3), () => Task.FromResult("second"));
Console.WriteLine($"{first} {second}");
```

The example prints `first second`, after the three-second gap.

The usage guide, with every option, every public entry point and every way each one fails, is `docs/Http.md` in the repository.
