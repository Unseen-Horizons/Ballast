# Ballast.AppUpdates: usage guide

Ballast.AppUpdates lets an app learn which of its builds exist and which are still supported, and decide what to say about its own
build. The API answers from configuration, per platform and per install channel. The app asks, and the rules here decide whether
its build is below the supported minimum, whether a newer build is offered, and what the platform's store says when the store can
tell. It depends on .NET, on `Microsoft.Extensions.Configuration.Abstractions`, on `Microsoft.Extensions.Logging.Abstractions`,
and on Ballast.Errors. It has no ASP.NET Core reference, so a MAUI app can reference the client and the rules, and the API uses the
configuration reader and the manager.

The package README has the install line and a runnable example. This guide covers every type and member, the defaults of every
option, the configuration shape, and every way each one fails.

## How the pieces fit

1. The API reads the build settings from its configuration (`AppVersionConfigReader`) and answers through `AppVersionManager`.
   The app's endpoint, which stays in the app, returns the answer as JSON with `Cache-Control: no-store`.
2. The app asks the endpoint with `AppVersionClient`. A failed ask is reported as not answered, and the app keeps what it knew.
3. The app's check service combines the answer with the store's answer (`IAppUpdater.IsUpdateAvailableAsync`, through
   `AppVersionRules.AskStoreAsync`), the installed build, and the rider's dismissal, and decides with `AppVersionRules.Decide`. It
   builds an `AppUpdateState` for the screen.

The library does not store anything, does not show anything, and does not start an update. Those are the app's: preferences, the
banner, and the platform's update flow (for example Google Play's in-app update) are implemented by the app or by Ballast.Maui.

## Configuration

The build settings are read from one section of configuration, `AppVersion` by default. Each platform has a child section, and a
platform may have a child section for each install channel. The keys are:

| Key | Meaning |
|---|---|
| `{Section}:{Platform}:LatestBuild` | The newest build that is published, a whole number above 0. A build below it is offered an update. |
| `{Section}:{Platform}:MinimumBuild` | The oldest build still supported, a whole number of 0 or more. 0 (or absent) means there is no minimum. |
| `{Section}:{Platform}:LatestVersion` | Text for the banner, for example `0.2.0`. Optional. |
| `{Section}:{Platform}:StoreUrl` | Where the install channel gets the build, as an absolute `https` link. Optional. A link that is not absolute `https` is dropped with a warning. |
| `{Section}:{Platform}:Messages:{language}` | Optional text for one UI language, for example `en` or `hr`. A blank entry is ignored. |
| `{Section}:{Platform}:{channel}:...` | The same keys for one install channel. |

A channel's section answers only when it has `LatestBuild` or `MinimumBuild`. Otherwise the platform's own section answers, so a
build that sends no channel keeps working. The two sections are never merged key by key: a channel that has numbers has its own
`StoreUrl` and messages too, or none.

A store channel may set only `MinimumBuild`. It then answers with `LatestBuild` 0, which means the server announces no newer
build and the store decides.

The configuration is read on every call. A settings change applies on the next request, after the restart the platform already
does.

## `AppUpdateOptions`

Set on the constructors of `AppVersionConfigReader`, `AppVersionManager` and `AppVersionClient`. Every default is what the apps
ship with, so an app that sets nothing keeps its wire.

| Option | Default | Used by | Meaning |
|---|---|---|---|
| `SectionName` | `AppVersion` | reader, manager | The configuration section the builds are read from. Must not be blank. |
| `Platforms` | `android` to `Android`, `ios` to `iOS`, `windows` to `Windows` | manager | The platforms the request may name, matched without case. The value is the name the configuration section uses, and it is the `Platform` the answer carries. Keys must not repeat with a different case. Must not be empty. |
| `Channels` | `play`, `direct`, `appstore` | manager | The install channels the request may name, matched without case and answered in lower case. Must not be empty, and no entry may be blank. |
| `StoreChannel` | `play` | `AppVersionRules.StoreUpdaterFor` (the app passes this value) | The channel whose store can say whether a newer build exists. Must not be blank. |
| `Route` | `app/version` | client | The path the client asks, relative to its base address. Must not be blank. |
| `PlatformUnknownCode` | `APP_PLATFORM_UNKNOWN` | manager | The code of the 400 for an unknown platform. |
| `ChannelUnknownCode` | `APP_CHANNEL_UNKNOWN` | manager | The code of the 400 for an unknown channel. |
| `NotConfiguredCode` | `APP_VERSION_NOT_CONFIGURED` | manager | The code of the 404 for a platform (or channel) with no build numbers. |
| `MisconfiguredCode` | `APP_VERSION_MISCONFIGURED` | manager | The code of the 500 for a configuration that does not hold together. |

`AppUpdateOptions` is validated when the reader, manager or client is built. A blank section name, route or code, an empty
platform or channel list, a blank channel, or a platform name that repeats with a different case throws `ArgumentException` at
construction.

## Server: `AppVersionConfigReader`

```csharp
public AppVersionConfigReader(IConfiguration configuration, AppUpdateOptions? options = null)
public ConfiguredAppVersion Read(string platformName, string? channel)
```

**`Read`** returns the raw values of the platform's section, or of the channel's section when that channel has numbers. Nothing is
parsed or checked here. A section with no values is returned with nulls, and `Messages` is empty. `Label` is the platform's name,
or the platform and the channel joined with a colon (`Android:direct`), and it is what the manager's log lines name.

`ConfiguredAppVersion` has `Label`, `Channel`, `LatestBuild`, `MinimumBuild`, `LatestVersion`, `StoreUrl` and `Messages`. The build
numbers are text here, as configured.

| Situation | Result |
|---|---|
| `configuration` is null | `ArgumentNullException` |
| `platformName` is null, empty or blank | `ArgumentException` |
| The platform has no section | All values null |
| A channel has no numbers | The platform's section answers, `Channel` is null |

## Server: `AppVersionManager`

```csharp
public AppVersionManager(IConfiguration configuration, ILogger<AppVersionManager> logger, AppUpdateOptions? options = null)
public AppVersionResponse Get(string? platform, string? channel = null)
```

**`Get`** answers for a platform, and for an install channel when one is given. The platform is matched without case and with
surrounding spaces trimmed. A blank channel is no channel. A channel is matched without case and answered in lower case, and
it is returned in `Channel` only when the channel's own section answered.

The rules, in order:

1. An unknown or blank platform is a 400 with `PlatformUnknownCode`, detail `platform must be one of: android, ios, windows.`
   (the names the options give).
2. A channel that is not in `Channels` is a 400 with `ChannelUnknownCode`, detail `channel must be one of: play, direct, appstore.`
3. A platform (or channel) with neither `LatestBuild` nor `MinimumBuild` is a 404 with `NotConfiguredCode`.
4. `LatestBuild`, when set, must be a whole number above 0. `MinimumBuild`, when set, must be a whole number of 0 or more.
5. A `MinimumBuild` above `LatestBuild` is refused. The configuration is never clamped.
6. `StoreUrl` must be an absolute `https` link. Otherwise it is dropped and logged as a warning, and the answer carries no link.

The answer's `Platform` is the configured name (for example `Android`), not the request's spelling.

| Situation | Result |
|---|---|
| `configuration` or `logger` is null | `ArgumentNullException` |
| An option is blank or repeated | `ArgumentException` at construction |
| Unknown or blank platform | `AppException`, 400, `PlatformUnknownCode`. Nothing is logged. |
| Unknown channel | `AppException`, 400, `ChannelUnknownCode`. Nothing is logged. |
| No numbers for the platform or channel | `AppException`, 404, `NotConfiguredCode`. Nothing is logged. |
| `LatestBuild` is not a whole number above 0 | `AppException`, 500, `MisconfiguredCode`. Logged as an error, naming the section and the value. |
| `MinimumBuild` is not a whole number of 0 or more | `AppException`, 500, `MisconfiguredCode`. Logged as an error. |
| `MinimumBuild` is above `LatestBuild` | `AppException`, 500, `MisconfiguredCode`. Logged as an error. |
| `StoreUrl` is not absolute `https` | Not an error. The answer carries no store link. Logged as a warning. |

The 500 answer's own text is generic (`The app version configuration is invalid.`), with no detail. The reason is in the log only.

## `AppVersionResponse`

The answer the endpoint returns. Its JSON names are `platform`, `latestBuild`, `minimumBuild`, `latestVersion`, `storeUrl`,
`channel` and `messages`. Installed apps read these names, so they do not change.

| Property | Meaning |
|---|---|
| `Platform` | The configured platform name. |
| `LatestBuild` | The newest published build. 0 when only a minimum is set. |
| `MinimumBuild` | The oldest supported build. 0 means no minimum. |
| `LatestVersion` | Display text, or null. |
| `StoreUrl` | An absolute `https` link, or null. |
| `Channel` | The channel whose section answered, or null for the platform-wide section. |
| `Messages` | Text by language code. Empty when none is set. |

Builds are compared as integers. Display text is never used for ordering.

## Client: `AppVersionClient`

```csharp
public AppVersionClient(HttpClient http, ILogger<AppVersionClient>? logger = null, AppUpdateOptions? options = null)
public Task<AppVersionAnswer> GetAsync(string platform, string? channel = null, CancellationToken ct = default)
```

The `HttpClient`'s base address is the API's root. The request is `GET {Route}?platform=…` with `&channel=…` when a channel is given.
Both values are escaped.

**`GetAsync`** returns an `AppVersionAnswer`, which is a record with `Answered` and `Info`:

| Answer from the server | `Answered` | `Info` | Meaning |
|---|---|---|---|
| 2xx with a usable build number (`LatestBuild` or `MinimumBuild` above 0) | true | the answer | The server's numbers. |
| 404 | true | null | "No minimum": the server has no numbers for the platform yet. |
| 2xx with no usable build number | false | null | Logged as a warning. The last kept answer still stands. |
| 2xx with a body that is not JSON | false | null | Logged as a warning. |
| Any other status (for example 500) | false | null | Logged as a warning, with the server's code and trace. |
| No connection, or a timeout | false | null | Logged as a warning. |

A 404 is logged at Information the first time for each client, and not again, so a platform that is not configured yet does not
log on every check. A 404 is an answer, so the app drops its last kept answer.

`GetAsync` never throws for a failed call. It throws only when `ct` is cancelled, and when `platform` is null, empty or blank
(`ArgumentException`).

The usable-number rule is the one the apps have always applied: an answer with `LatestBuild` 0 and `MinimumBuild` 0 is not an
answer.

## Rules: `AppVersionRules`

A static class of pure functions. None of them reads a setting; they take the values.

| Member | What it decides |
|---|---|
| `CheckInterval` (6 hours) | How long a successful check stays fresh. |
| `Decide(int currentBuild, AppVersionResponse info, int? dismissedLatestBuild)` | The decision from the server alone. |
| `Decide(int currentBuild, AppVersionResponse? info, StoreUpdate? store, int? dismissedBuild)` | The decision with the store's answer. Required comes from the server's minimum only and wins over a dismissal. When the store answered, it alone decides the banner. When it did not, the server's latest build decides. |
| `BannerFor(AppUpdateDecision)` | The banner: `Required` for a required build, `Offer` for an available one, `None` otherwise. |
| `CanDismiss(UpdateBannerKind)` | True only for `Offer`. A required banner cannot be dismissed. |
| `AnnouncedBuild(int currentBuild, AppVersionResponse? info, StoreUpdate? store)` | The build number the banner announces, or 0. The app stores a dismissal against this number. |
| `StoreUpdaterFor(string? channel, IAppUpdater? updater, string storeChannel = InstallChannels.Play)` | The updater to ask, which is the updater only when `channel` is the store channel. Any other channel, and null, consults nothing. |
| `AskStoreAsync(IAppUpdater? updater, TimeSpan timeout, ILogger? logger = null, CancellationToken ct = default)` | Asks the store within `timeout`. Returns null when there is no updater, the store cannot tell, the answer has no build number for an available update, the wait times out, or the call throws. Each reason is logged as a warning when a logger is given. A cancelled `ct` is not swallowed. |
| `ShouldCheck(DateTime? lastCheckUtc, DateTime nowUtc, bool lastWasRequired)` | True when there is no earlier check, when the last check is older than `CheckInterval`, when the last check is in the future, or when the last answer was required. |
| `PickMessage(IReadOnlyDictionary<string,string>? messages, string uiLanguage)` | The message for exactly that language, trimmed. Null when there is none. Another language's text is never returned. |

The decision table, in order:

1. A build below `MinimumBuild` is `UpdateRequired`. Nothing else is consulted.
2. The announced build is the store's when the store answered (its build number, when it is newer than the installed build), and
   the server's `LatestBuild` otherwise. If that is not above the installed build, the build is `UpToDate`.
3. A dismissal at or above the announced build makes it `UpdateAvailableDismissed`. Otherwise it is `UpdateAvailable`.

`UpdateRequired` is announced, never enforced. The library blocks nothing.

## Store and install channel

| Type | Meaning |
|---|---|
| `IAppUpdater` | Starts an update and says whether the store has a newer build. The platform implements it. `CanStart(storeUrl)` says whether the update button has anything to open. `StartAsync(storeUrl)` returns an `AppUpdateStartResult`. `IsUpdateAvailableAsync()` returns a `StoreUpdate?`, and its default answers null (the platform cannot tell). |
| `AppUpdateStartResult` | `Started`, `NoStoreLink` (nothing to open), `Failed` (already logged). |
| `StoreUpdate` | `Available` and `VersionCode`: the store's newest build number, the same number space as the installed build. `VersionCode` is 0 when there is no update. |
| `InstallChannels` | The default channel names: `Play` (`play`), `Direct` (`direct`), `AppStore` (`appstore`). `FromAndroidInstaller(string?)` maps Android's installer package name (`com.android.vending` is `Play`, anything else is `Direct`). `IsStore(string?)` is true for `Play` and `AppStore`, and is used only for wording. |

The install channel of this install (`IInstallChannel`, and its fixed version for a platform with one channel) is not in this library: it belongs to Ballast.Maui, where the platform reads it. The channel names above are what it reports.

`AskStoreAsync` is how the app asks an updater. A timeout of a few seconds is typical: the store's answer must not hold up the
check, and the server's rule decides when the store does not answer in time.

Ballast.Maui's `AppVersionService` passes its optional `ILogger` (its last constructor parameter, null by default) to
`AskStoreAsync`, so each reason the store's answer is not used (no answer, a failed call, no build number) is logged once per
check. Its "No update check" line is written to `Trace`, and names the platform from `IUpdateDevice.PlatformName` (null by
default, so the line says "this platform").

## State: `AppUpdateState`

A record the app builds for its screen: `Decision`, `CurrentBuild`, `LatestBuild`, `LatestVersion`, `StoreUrl`, `Message`, `Channel`.
`None` is the state before anything was learned. `IsRequired`, `IsAvailable` and `IsDirect` are read from it. The library does not
build it; the app's check service does, from the rules above.

## What the app does, and what the library does not

- The endpoint: a `[AllowAnonymous]` action that calls `AppVersionManager.Get` and sets `Cache-Control: no-store`. It is anonymous
  on purpose, so a build too old to sign in can still be told to update.
- The configuration itself: the settings, in the app's configuration or in its hosting environment.
- The check service: when to ask (the interval, the resume, the required rule), what to keep on the device, and how a dismissal is
  stored.
- The platform's updater (for example Google Play's in-app update) and the install-channel detection on the device.
- The banner, its words, and the store link button.
- The build number itself, which the app stamps into its own build.

## Notes on the wire

- The answer's JSON names are the ones above, in camelCase. A null is written as `null`.
- The client reads the answer with a source-generated deserializer, so the library works in a trimmed or AOT-compiled app.
- The errors carry the codes in `AppUpdateOptions`. The app's endpoint turns each `AppException` into a problem answer (Ballast.AspNetCore
  writes it), with the code, a title and a trace reference.
