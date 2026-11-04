# Ballast.AppUpdates

The app's own build check. The API answers which builds exist and which are still supported, per platform and per install
channel, from configuration. The app asks, and decides what to say about its build: whether a newer one is offered, whether
its build is below the supported minimum, and what the store says when the store can tell. It has no ASP.NET Core reference,
so a MAUI app can use the client and the rules, and the API uses the manager.

Install:

```
dotnet add package Ballast.AppUpdates --prerelease
```

## Example

```csharp
using Ballast.AppUpdates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

// The build numbers come from configuration. Build 18 is below the minimum of 20, so the answer is that it is required.
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AppVersion:Android:LatestBuild"] = "24",
        ["AppVersion:Android:MinimumBuild"] = "20",
    })
    .Build();
var manager = new AppVersionManager(configuration, NullLogger<AppVersionManager>.Instance);
var answer = manager.Get("android");
Console.WriteLine(answer.LatestBuild);

var decision = AppVersionRules.Decide(18, answer, dismissedLatestBuild: null);
Console.WriteLine(decision);
```

The example prints `24` and then `UpdateRequired`. The configuration shape is the one the apps are set up with:
`AppVersion:{Platform}:LatestBuild`, `MinimumBuild`, `LatestVersion`, `StoreUrl`, and `Messages:{language}`, with the same keys
under `AppVersion:{Platform}:{channel}` for an install channel of its own.

The client side asks the API with `AppVersionClient`, and the decisions are in `AppVersionRules`. The usage guide, `docs/AppUpdates.md`
in the repository, has every option with its default, every public member, and every way it fails.
