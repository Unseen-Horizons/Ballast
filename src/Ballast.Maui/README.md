# Ballast.Maui

The plumbing a .NET MAUI phone app needs and that does not belong to the app's product: on-device storage sealed with one
key, connectivity state, HTTP client setup, the AdMob banner, the localisation runtime, crash reports, permission asks, the
store review prompt policy and a few view helpers. Every MAUI type sits behind an interface, so the logic runs on `net10.0`
and the platform code is compiled only for Android and Windows.

Preview: version 0.1.0-preview.1. The usage guide, with every option and every way the library fails, is `docs/Maui.md` in
the repository.

Install:

```
dotnet add package Ballast.Maui --prerelease
```

## Example

```csharp
using System.Text.Json;
using Ballast.Maui.Ads;
using Ballast.Maui.Permissions;
using Ballast.Maui.Diagnostics;

// Ads stay off unless the switch is on. A release build needs a well-formed banner id, and a debug build uses the test units.
var document = JsonDocument.Parse("{\"Ads\":{\"Enabled\":true,\"AndroidBannerAdUnitId\":\"ca-app-pub-1234567890123456/1234567890\"}}");
var ads = AdsOptions.Read(document) with { PlatformSupportsAds = true, IsDebugBuild = false };
Console.WriteLine(ads.CanInitialize);            // True
Console.WriteLine(ads.EffectiveBannerAdUnitId);  // ca-app-pub-1234567890123456/1234567890

// Location: ask the system while it still shows its prompt, and send the person to settings once it will not.
Console.WriteLine(LocationAskPolicy.Decide(allowed: false, showRationale: false, requestedOnThisInstall: true)); // OpenSettings

// A crash report is scrubbed before anything leaves the phone: an email and a link keep only their placeholder and host.
Console.WriteLine(CrashReportText.Scrub("Sign-in failed for ana@example.com at https://api.example.com/v1/login"));
// Sign-in failed for [email] at [link: api.example.com]
```

The example prints `True`, the banner id, `OpenSettings`, and the scrubbed line.
