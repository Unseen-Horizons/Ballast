# Ballast.Maui usage guide

Ballast.Maui is the plumbing a .NET MAUI phone app needs that is not the app's product: on-device storage sealed with one
key, connectivity state, HTTP client setup, an AdMob banner and native cards, the localisation runtime, crash reports,
permission asks, the store review prompt policy, and a few view and collection helpers.

Status: 0.1.0-preview.1, not yet published. The package targets `net10.0` for the logic, which the tests run on, and
`net10.0-android` (and `net10.0-windows10.0.19041.0` when built on Windows) for the MAUI and platform code. Every MAUI type
sits behind an interface that the `net10.0` code uses, so the logic runs without a device.

```
dotnet add package Ballast.Maui --prerelease
```

The package README has a short example that runs as written. This guide covers every option, every public entry point, and
every way each piece fails.

## Conventions

- Every option has a default, and the default is safe: ads are off, certificate checks are on, a missing setting keeps the
  app running.
- Nothing in the library reads a build symbol. A debug or release difference comes in through an option (`IsDebugBuild`,
  `AllowDangerousCertificates`), so the app decides it from its own build configuration.
- Nothing names an app. Every app value (preference keys, folder names, unit ids, resource names, page type names) comes in
  through an options record or an interface.
- JSON is written and read through a `JsonTypeInfo<T>` the app passes in, from its own source-generated context. The library
  uses no reflection for serialisation.
- Where a call can fail on the device, it either returns an answer that says so (`null`, `false`, a missing value) or logs
  through `IClientLog`. Each section says which, and each failure mode is listed.

---

## Storage

Sealed files are AES-256-GCM with a fresh 12-byte nonce for each write. The layout is nonce, then tag, then ciphertext.

### `SealedPayload`

Pure format functions, usable without a store.

| Member | Behaviour |
|---|---|
| `Seal(key, text)` / `Open(key, bytes)` | Text as UTF-8 under one key. |
| `SealBytes(key, bytes)` / `OpenBytes(key, bytes)` | Binary content, the same layout. |
| `NonceBytes` (12), `TagBytes` (16) | The format's constants. |

**Failure modes.** `Open` throws `CryptographicException` when the file is shorter than nonce plus tag, when it was altered,
or when it was sealed under another key. `AuthenticationTagMismatchException` (a subclass) is what .NET throws for an altered
file, so catch `CryptographicException`, not the subclass alone.

### `SealedKeyProvider`

The one key of an app's stores. Every store asks it, so every store seals with the same key.

| Option or member | Default | Notes |
|---|---|---|
| `keyName` (constructor) | none | The secure-store name the key is kept under. The library has no default: each app names its own. Blank is refused (`ArgumentException`). |
| `KeyBytes` | 32 | 256 bits. |
| `GetKeyAsync(ct)` | | Reads the key from the secure store, or makes one and stores it. Concurrent first callers get the same key, and it is written once. |
| `ClearAsync(ct)` | | Removes the key from the secure store and from memory. Whatever is still sealed under it becomes unreadable, so delete the files first. |

**Failure modes.**
- A stored key that cannot be read (a restored phone, whose keystore key did not come back) is treated as absent. A fresh key
  is made, and the secure store is cleared once through `SecureValueReader`. Nothing is substituted for the key.
- A stored key that is not valid base64 throws `FormatException`. It is never replaced by a guess.

### `SecureValueReader`

Reads secure values without failing the screen that asks.

| Member | Behaviour |
|---|---|
| `GetAsync(key, ct)` | The value, or `null` when it is absent or cannot be read. A failed read is logged (warning), then the whole secure store is cleared once for this reader. A cancelled read propagates the cancellation. |
| `ClearOnce()` | Clears the store the first time a read fails for this reader. A failed clear is logged and never thrown. |
| `RearmClear()` | Lets the next failed read clear the store again (tests). |

Each reader clears at most once. Build one reader per process and share it, as the app's key provider does.

### `ISecureValues` and `IPreferenceStore`

The two storage seams. The MAUI implementations are `MauiSecureValues` (wraps `SecureStorage`) and `MauiPreferences`
(wraps `Preferences`, with an optional `sharedName` container). An app's tests supply its own in-memory fakes.

### `OwnedStoreOptions` and `OwnedSealedStore`

Per-owner sealed JSON files: one folder for each owner, each file an AES-GCM sealed document.

| Option | Default | Notes |
|---|---|---|
| `FolderName` | none (required) | One folder name under the app's data folder. A path or a separator is refused. |
| `FileExtension` | `.bin` | Must start with a dot and name no folder. |
| `OwnerDigits` | 32 | How many hex characters of the owner's SHA-256 name the folder. Between 16 and 64, or `ArgumentOutOfRangeException`. |

| Member | Behaviour |
|---|---|
| `SaveAsync(owner, name, value, typeInfo, ct)` | Writes the sealed file beside its target and moves it into place, so the file appears whole or not at all. Returns `true`. |
| `ReadAsync(owner, name, typeInfo, ct)` | The value, or `null`. |
| `Clear(owner)` | Deletes one owner's folder. Other owners are kept. |
| `HasAnyData()` | Whether anything remains in the store's folder. |
| `DirectoryFor(owner)` / `PathFor(owner, name)` / `RootFolder` | Where the files live. |

The owner key is hashed into the folder name, so a user id never appears in a path.

**Failure modes.**
- A write that cannot happen (a blocked folder, a full disk) returns `false` and is logged. The caller keeps its previous
  data and shows its ordinary state.
- A read of a file that is missing returns `null` without a log line.
- A read of a file that is corrupt, tampered with, or sealed under a key that no longer exists returns `null` and is logged.
  The file is left where it is.
- A blank owner key or a name with a path separator throws `ArgumentException`.

### `InstallAnswers`

The install's own answers (first-start questions, a permission that was asked), kept in their own preferences container.

| Member | Behaviour |
|---|---|
| `new InstallAnswers(preferences, sharedName, log)` | `sharedName` is required and names the container. |
| `Get(key, missing, unreadable)` | The stored value, `missing` when there is none, `unreadable` when the container cannot be read (logged). |
| `Set(key, value)` / `Remove(key)` | A container that cannot be written is logged, never thrown. |

The container must be excluded from the app's backup (see "Setup outside the code"). Android does not restore runtime
permission grants, so an answer restored with a reinstall would skip a question the phone no longer has.

### `StrayTempFiles`

A one-time sweep of fixed-name temporary files left by older writers.

| Member | Default | Behaviour |
|---|---|---|
| `DefaultMinimumAge` | 10 minutes | A file younger than this is left alone: another copy of the app may still hold it during a deploy. |
| `Sweep(folders, suffix, minimumAge, log)` | | Removes the old matching files, recursively. Returns what was removed, how many were too young, and which failed. A missing folder has nothing to sweep. |
| `RunOnce(folders, suffix, preferences, markerKey, sharedName, log, minimumAge)` | | Sweeps unless the marker says it already completed. The marker is set only when nothing young remained and nothing failed, so an incomplete sweep runs again at the next start. Returns `null` when the marker is already set. |

The suffix must start with a dot (`.tmp`). The current atomic writers use `.atomic-tmp`, which never matches the old suffix.

---

## Connectivity

### `IConnectivitySource` and `OfflineModeState`

| Member | Behaviour |
|---|---|
| `IConnectivitySource` | `Changed` (raised with the offline flag), `IsOfflineNow()`, `IsOnCellular()`. |
| `MauiConnectivitySource` | The MAUI implementation over `Connectivity`. It is `IDisposable`: dispose it when the app is torn down. |
| `new OfflineModeState(source)` | Starts from the current value, follows changes both ways, and holds one subscription. |
| `IsOffline`, `IsExpanded` | Bindable. `INotifyPropertyChanged` is raised on the thread that built the state. |
| `Dispose()` | Stops listening. Safe to call twice. |

Create one state for the app, as a singleton, so there is one subscription. A second state adds a second subscription.

---

## HTTP

### `MauiHttpOptions`

| Option | Default | Notes |
|---|---|---|
| `HandlerLifetime` | 10 minutes | How long an `IHttpClientFactory` handler is reused. |
| `AllowDangerousCertificates` | `false` | Accepts any server certificate. Only for a developer's local server. See below. |

### `MauiHttpClients` and `DangerousCertificates`

| Member | Behaviour |
|---|---|
| `Create(baseAddress, timeout, options, build)` | A client for a singleton that owns its handler. `timeout` must be positive. The caller owns the client. |
| `services.AddNamedClient(name, configure, options, build)` | Registers a named client with the handler lifetime and the primary handler. Returns the builder, so the app adds its own handlers. |
| `DangerousCertificates.CreateHandler(allowed, build)` | With `allowed` false, the system's own trust. With `allowed` true, a handler that accepts every certificate. |

`IRuntimeBuild.IsDebuggable` is a `bool?`. `false` means the build is not debuggable. `null` means the platform did not say,
and is not refused.

**Failure modes.**
- `AllowDangerousCertificates = true` in a build that reports itself not debuggable throws `InvalidOperationException` at
  registration (`AddNamedClient`) or at `Create`, so the mistake stops start-up rather than shipping.
- A non-positive timeout throws `ArgumentOutOfRangeException`.

---

## Ads

The ads are off unless the app turns them on. Nothing is requested while they are off, and no consent answer is read.

### `AdsOptions`

| Option | Default | Notes |
|---|---|---|
| `Enabled` | `false` | Only a JSON `true` switches the ads on. A string `"true"` does not. |
| `AndroidBannerAdUnitId` | `""` | Form `ca-app-pub-` + 16 digits + `/` + 10 digits. A release build needs a well-formed one to start. |
| `AndroidNativeAdUnitId` | `""` | The same form. Empty turns the native cards off and leaves the banner alone. |
| `IsDebugBuild` | `false` | A debug build always uses the network's test units, and never a production id. |
| `PlatformSupportsAds` | `false` | Set by the platform registration (Android). Off elsewhere, where no ad is requested. |
| `AdFreePageTypeNames` | `[]` | Page type names that never show the banner. A renamed page must be renamed here too. |
| `BadgeText` | `"Ad"` | The label on a native card. |
| `DividerLightResourceKey` | `"BorderSubtleLight"` | Theme resource key of the banner's divider, light theme. |
| `DividerDarkResourceKey` | `"DarkBorder"` | The same, dark theme. |
| `LogTag` | `"Ballast.Maui.Ads"` | The platform log tag, so the ads can be filtered in logcat. |

Computed: `CanInitialize` (platform supports ads, enabled, and either a debug build or a valid banner id),
`EffectiveBannerAdUnitId` and `EffectiveNativeAdUnitId` (`null` in a debug build, so the network's test unit is used),
`IsNativeEnabled`, `HasValidProductionBannerId`, `HasValidProductionNativeId`.

`AdsOptions.Read(document, sectionName = "Ads")` reads the switch and the two ids from a parsed settings document. A missing
document, a missing section, or a section that is not an object gives the disabled defaults. A missing or non-string id is
empty, and a present one is trimmed.

### `AdUnitIdSyntax`

| Member | Behaviour |
|---|---|
| `IsValidBannerId(id)` / `IsValidNativeId(id)` | The form check. An app id (with `~`) is not a unit id, and is refused. |
| `ReleaseConfigurationError(enabled, bannerId, nativeId, sectionName = "Ads")` | `null` when the configuration is fine. Otherwise a message that names the field that is wrong. Call it at start-up in a release build and log the result. A malformed native id is ignored while the ads are off. |

### `AdsService` and `IAdsService`

| Member | Behaviour |
|---|---|
| `new AdsService(options, consent, log, dispatchToUi)` | `consent` may be `null`. `log` and `dispatchToUi` default to the debugger trace and to running inline. |
| `IsEnabled`, `IsNativeEnabled`, `BannerAdUnitId`, `NativeAdUnitId` | From the options. |
| `CanRequestAds` | The consent SDK's answer, asked on each read. |
| `IsPrivacyOptionsRequired` | Asked once and kept until the consent changes. |
| `ShowPrivacyOptions()` | Shows the privacy form on the UI thread, when it is required. |
| `CanRequestAdsChanged` | Raised when the consent answer changes, or the form closes. |

The consent SDK is an `IAdConsent`. The Android implementation is `AdMobConsent`, which wraps Plugin.AdMob's consent service.
A test uses an in-memory one.

**Failure modes.**
- With no consent SDK, `CanRequestAds` is `false`. Nothing is requested on an answer nobody has.
- With the ads off, the consent SDK is ignored and no consent event is logged.
- A failed read of the consent answer is logged (`[Ads] Could not read whether ads may be requested: ...`) and reads as `false`.
- A failed read of the privacy answer is logged and reads as `false`. It is not kept, so the next call asks again.
- Every consent event is logged through `ConsentLog`: an update with its status, an update that failed, a form that could not
  be shown. A value the SDK did not give is said to be missing.

### `ConsentLog`

The text of each log line: `InfoUpdated(status, canRequestAds)`, `InfoFailedToUpdate(message)`, `FormError(message)`. Each
line starts with `[Ads]`.

### `NativeAdLoadRule`

| Member | Default | Behaviour |
|---|---|---|
| `DefaultMinimumInterval` | 60 seconds | The least time between two native card loads. |
| `MayStartLoad(sinceLastStart, minimumInterval)` | | `true` for a first load (`null`), and after the interval. A negative elapsed time is allowed. A negative interval throws `ArgumentOutOfRangeException`. |

### `AdBannerFrame`

`GapDp` (8), `DividerDp` (1). `Height(adaptiveDp)` is the banner's total height: a gap, a divider, the ad, and a gap. It is 0
for a collapsed slot (`null` or 0, which is what a failed request leaves).

### `ContentWrapper<TContent>`

Wraps a page's content once, however often the page's content mapping re-enters.

| Member | Behaviour |
|---|---|
| `EnsureWrapped(get, set, wrap)` | Wraps the current content unless it is already wrapped, and returns whether it is. A re-entered call during the wrap does nothing. |
| `IsWrapped` | Set before the wrapper is assigned as content. |

A failing `wrap` gives the page its own content back and leaves the wrapper unset.

### MAUI page hooks

| Type | Behaviour |
|---|---|
| `AdsPageDecorator.RegisterPageHook(options)` | Registers the page hook from the app's start-up. Pages named in `AdFreePageTypeNames` are not wrapped. |
| `AdsPageDecorator.IsAdFree(page)` | Whether a page's type name is ad-free. |
| `AdsPageDecorator.Attach(page, host)` / `Adopt(page, layout, row)` | Puts the banner host on a page. |
| `AdBannerHost` | Moves the one banner between a page's grid row (`MoveTo`) and detaches it (`Detach`). `IsEnabled` follows the service. |
| `AdBannerSlot` | The view that holds the banner and its divider. Its colours come from the options' resource keys. |

---

## Localisation

### `LocalizationOptions`

| Option | Default | Notes |
|---|---|---|
| `ResourceBaseName` | none (required) | The base name of the `.resx` resources, for example `App.Strings`. |
| `ResourceAssembly` | none (required) | The assembly that holds the resources. |
| `SupportedLanguages` | `["en"]` | The languages the app offers. The first one is the fallback. |
| `Cultures` | `en` to `en-US` | Maps a language to a culture name. A language not listed is used as its own culture name. |
| `PreferenceKey` | `"app_language"` | Where the chosen language is saved. |
| `PreferencesSharedName` | `null` | The preferences container of that key. |
| `PluralKey` | `null` | `(baseKey, count, language) => key`. When unset, `baseKey_One` is used for a count of 1 and `baseKey_Other` for every other count. |

### `LocalizationService`

| Member | Behaviour |
|---|---|
| `LocalizationService.Initialize(options, preferences)` | Makes the process-wide instance, shows the saved language, and returns it. Once per process. |
| `LocalizationService.Instance` | The instance from `Initialize`. |
| `LocalizationService.Create(options, preferences)` | A separate instance, for tests or an app that keeps its own. `preferences` may be `null`: the choice is then kept in memory. |
| `this[key]` / `GetString(key)` | The string in the current culture. |
| `Counted(baseKey, count, args)` | A string in the plural form for `count`. |
| `SetCulture(culture)` | Changes the culture, makes it the default for every thread, saves the choice, and raises `CultureChanged`. |
| `RestoreSavedCulture()` | The saved language, or the device's when none is saved, or the first supported language when the device's is not offered. |
| `Strings` | `LocalizedStrings`, the indexer that XAML bindings listen to. |

**Failure modes.**
- A missing key, or a resource table that cannot be read, is shown as the key itself.
- A saved language the app does not offer falls back to the first supported language.
- `Initialize` called twice throws `InvalidOperationException`. `Instance` before `Initialize` throws it too.
- `SetCulture(null)` throws `ArgumentNullException`.

`TranslateExtension` (`{ext:Translate Key=...}`) binds a view to a key and re-reads when the culture changes. Strings set in
code (a tab title read once in a constructor) do not change until the app rebuilds them.

---

## Diagnostics and crash reports

### `IClientLog` and `ClientLog`

| Member | Behaviour |
|---|---|
| `Failure(source, summary, code, detail, traceId, ex)` | Traces the line, and keeps the failure for a crash report: source, summary, code, trace id and exception type only. The detail and the exception message are never kept. |
| `Warning(source, summary)` | Traces the line. Not kept. |
| `Recent` | The `RecentFailureLog`. |
| `new ClientLog(recent, write, clock)` | The debugger trace, a log of 30 failures, and the system clock by default. |

### `RecentFailure` and `RecentFailureLog`

`RecentFailure.Create(timeUtc, source, summary, code, traceId, exceptionType)` scrubs the summary and caps it at 100
characters. A blank code or trace id is kept as `null`. `RecentFailureLog` keeps the last 30 (`Capacity`), oldest dropped
first, and is safe to add to from many threads.

### `CrashReportText`

| Member | Behaviour |
|---|---|
| `Scrub(text)` | Takes out what could place the person. A web address keeps its host only (`[link: host]`). A position, a coordinate pair, a long decimal, an absolute path or a GUID is replaced by a marker. An email, a phone number or a link is masked last, by `PersonalDataMasker` from Ballast.Text. |
| `Clip(text, max)` | Cuts to at most `max` characters, with an ellipsis, without splitting a surrogate pair. |
| `Compose(input, maxLength = 4000)` | The report: the exception and its inner exceptions, the build and the system, the time, the hook, and the recent failures. Oldest failures are dropped first, then stack frames, and only then the text is cut, with `[cut to fit]`. |
| `SubjectFor(report)` | The first line, cut to 200 characters. |
| `MaxLength` (4000), `MaxSubjectLength` (200) | The feedback endpoint's limits. The app must keep its endpoint at least this large. |

The masker over-scrubs: a run of seven or more digits is taken for a phone number, unless it is a date. A harmless number costs a
little detail; a missed position would cost privacy, so the rules lean to hiding.

### `CrashReportStore`

One waiting report, in `pending-crash.txt` in the given directory.

| Member | Behaviour |
|---|---|
| `Save(text)` | Writes atomically. A newer report replaces an older unsent one. Returns `false` when it cannot be written. |
| `ReadPending()` / `HasPending` / `Delete()` | Read, check, and delete. `Delete()` with nothing saved returns `true`. |

A half-written temporary file is never a pending report.

### `CrashReporter`

Writes one report per exception. Recording never throws.

| Member | Behaviour |
|---|---|
| `new CrashReporter(store, info, log)` | `info` is an `IAppBuildInfo`. |
| `Record(exception, source)` | Writes the report. Returns `true` when a report for this exception is on disk. The same exception through two hooks is written once. A platform value that cannot be read is written as `unknown`. |
| `Install()` | Hooks `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`. Only the first installer in the process is used. |

An unobserved task exception is not a crash report: a task nobody awaited does not close the app. It is logged as a handled
failure instead, and rides along on a later report. The Android runtime's own hook is `AndroidCrashHooks.Attach`.

Native crashes and freezes are not .NET exceptions, and this does not see them.

### `CrashReportOffer` and `CrashReportStartup`

| Member | Behaviour |
|---|---|
| `CrashReportOffer.RunAsync()` | Offers the waiting report once per run. Sends it only when the person says Send, and deletes the file on Send, on Don't send. A failed send, or a question that was closed without an answer, keeps the file for the next start. Returns a `CrashOfferResult`. |
| `CrashReportStartup.TryOfferAsync()` | Waits for a settled screen, then runs the offer. It marks the dialog clock, so the review prompt does not follow at once. |
| `CrashReportStrings` | The question and the buttons. English defaults. The app passes its own translated copy. |
| `MauiCrashReportUi` | The MAUI dialogs for the offer. |

**Failure mode.** The send function is the app's. If it throws, `RunAsync` throws and the file is kept. The caller must catch
it, or the next start shows the question again.

---

## Permissions

### `LocationAskPolicy`

`Decide(allowed, showRationale, requestedOnThisInstall)` returns what the locate button asks:
- `AlreadyAllowed` when the location is allowed.
- `SystemPrompt` while the platform will show its prompt: after one refusal, or when this install has not asked yet.
- `OpenSettings` once the platform will not show its prompt and this install has asked before.

### `LocationPermission`

| Member | Behaviour |
|---|---|
| `new LocationPermission(source, answers, platform, log)` | `source` is an `IPermissionSource`; `MauiPermissionSource` is the MAUI one. |
| `IsLocationAllowedAsync()` | Granted, or `Unknown` on Windows (where there is no grant to ask for). On iOS `Unknown` means not asked yet, so it is not allowed. |
| `RequestLocationAsync()` | Records that the prompt is being shown, before it is shown, and then asks. Returns whether the location is allowed after the request. |
| `ShouldShowLocationRationaleAsync()` | Whether the platform wants a rationale before the next ask. |
| `LocationRequestedOnThisInstall` | Read from `InstallAnswers`, under `permissions.locationRequested`. |

**Failure modes.**
- A failed check (`IsLocationAllowedAsync`) is logged and reads as allowed: there is then nothing the app could ask.
- A failed request (`RequestLocationAsync`) is logged and reads as `false`.
- A failed rationale check is logged and reads as `false`, which leaves the decision to `LocationRequestedOnThisInstall`.

### `NotificationPermission` (Android)

`NotificationPermission.RequestAsync()` asks for the notification permission on Android 13 and later, and returns whether
notifications may be posted after the ask. On earlier versions it asks nothing and returns whether notifications are enabled
in the system settings. Call it on the UI thread.

---

## Store review prompt

### `ReviewPromptPolicy`

Pure rules, checked in this order by `FirstBlock`. The first one that holds is the reason.

| Rule | Value |
|---|---|
| The install came from the store's Play channel | required (`NotPlayInstall`) |
| Not the first session | required (`FirstSession`) |
| At least 14 days since the first run | `MinimumSinceFirstRun` |
| At least 3 finished journeys | `MinimumFinishedJourneys` |
| The page on screen is not one that must stay free of prompts | `OnExcludedPage` |
| No dialog in the last 5 minutes | `DialogGap` |
| At least 90 days since the last ask | `MinimumSinceLastAsk` |
| Fewer than 3 asks on this install | `MaximumAsks` |

### `ReviewPromptService`

| Member | Behaviour |
|---|---|
| `ReviewPromptOptions` | The state keys: `review.firstRunUtc`, `review.firstSession`, `review.finishedTrips`, `review.lastAskUtc`, `review.askCount`, and `SettleDelay` (3 seconds by default). |
| `Start()` | Each start. Stamps the first run and this session, once per install. |
| `JourneyFinished()` | Counts the journey, then, after the settle delay and a settled screen, asks if the policy allows. Fire and forget: it never throws. |
| `LastAskUtc()` | When the store prompt was last launched, or `null`. |
| `IStoreReviewPrompt` | `RequestAsync()` launches the store's rating flow. `NoStoreReviewPrompt` launches nothing, for an install that is not from the store. |

The ask is recorded before the flow is launched, so an ask the store then skips still counts against the limits.

The dialog clock (`DialogClock`) is process-wide. A second ask within five minutes is refused, which is the intent.

---

## Platform

### `IAppBuildInfo`

`VersionString`, `BuildString`, `PlatformName`, `OsVersion`. `MauiAppInfo` is the MAUI implementation. A platform value that
cannot be read makes the crash report say `unknown` rather than fail.

### `IInstallChannel`, `InstallChannels` and `FixedInstallChannel`

| Member | Behaviour |
|---|---|
| `InstallChannels.Play` (`"play"`), `Direct` (`"direct"`), `AppStore` (`"appstore"`) | The channel names. |
| `InstallChannels.FromAndroidInstaller(package)` | `com.android.vending` is Play, after trimming the package name; the comparison is case-sensitive. Anything else, or a blank package, is Direct. |
| `InstallChannels.IsStore(channel)` | Play and AppStore. |
| `AndroidInstallChannel` | Reads the installer package from the package manager. |
| `FixedInstallChannel(channel)` | A channel that is given, for tests. |

### `IScreenBrightness` and `AndroidScreenBrightness`

`SetFull()` raises the screen to full brightness, and `Restore()` puts it back. Both run on the main thread.

---

## Helpers

### `Debouncer`

`new Debouncer(delay)`. `Call(action)` runs the last action of a burst, after `delay`, on the caller's synchronisation context.
`Cancel()` and `Dispose()` drop a waiting action. A new call restarts the wait. A throwing action does not stop the next
call. The delay must not be negative.

### `CollectionSync`

| Member | Behaviour |
|---|---|
| `Align(list, target)` | Makes an `ObservableCollection` equal to the target, touching only what differs (inserts, removes, moves). An identical target raises no event. |
| `Reuse(current, source, matches, create)` | Builds the rows for a source, reusing each row that still shows its element. A row whose element is gone is dropped. Equal elements never share one row. |

### `FirstUse`

`Take(wanted)` is `true` once, the first time something is wanted, and `false` after that. `IsTaken` says whether it has.
Used to build a lazily shown section once.

### `PackagedSettings` and `IAppPackage`

Reads the packaged settings file (`appsettings.json` by default) once, and shares the parsed `JsonDocument` with every reader.
`MauiAppPackage` is the MAUI implementation.

**Failure mode.** A missing or malformed file is logged as a warning and `Document` is `null`. Every reader then falls back to
its compiled-in default, so an unreadable settings file does not stop the app starting.

### `TypeRegistrationScan`

`Select(types, pageNamespace, pageBase, viewModelNamespace, viewModelBase)` picks the concrete (non-abstract) pages and view
models from a type list in one pass. An app uses it in place of two separate scans.

---

## Dialogs

### `SettledScreen` and `MauiSettledScreen`

`new SettledScreen(isSettledOnUiThread, attempts = 60, pause = 500 ms)`. `WaitAsync()` checks until the screen is settled, and
returns `true`. It returns `false` when the attempts run out. `attempts` must be at least 1, and `pause` must not be negative.
`MauiSettledScreen.Create()` asks the navigation shell.

### `DialogClock`

`Mark()` records that a dialog was just shown. `LastUtc` is when it was last marked, or `null`. The review prompt reads it.

---

## Views

- `LazySection<TView>` builds a view the first time it is wanted (`Ensure(wanted)`), and keeps it after that.
- `ThemeColors.Get(key, missing)` reads a theme colour by resource key. A missing key gives `missing`, which defaults to a
  magenta colour that is easy to see in a debug build.
- `TranslateExtension` is described under Localisation.

---

## Setup outside the code

These are needed in the app, not in the library.

**AdMob.** The app id goes in `AndroidManifest.xml`, inside `<application>`:

```xml
<meta-data android:name="com.google.android.gms.ads.APPLICATION_ID" android:value="ca-app-pub-XXXXXXXXXXXXXXXX~YYYYYYYYYY" />
```

The app id uses `~`. The ad unit ids (`AndroidBannerAdUnitId`, `AndroidNativeAdUnitId`) use `/`, and `AdUnitIdSyntax` refuses
an app id in their place. Create the ad units in the AdMob console. A debug build needs no real ids: it uses the network's
test units.

**Backup.** Android Auto Backup restores the settings file that names a secure value, but not the keystore key that encrypts
it, and it does not restore runtime permission grants. Exclude the install answers container (the file
`shared_prefs/<sharedName>.xml`) and the secure-store files from backup, in the app's `fullBackupContent` rules (Android 11
and earlier) and its `dataExtractionRules` (Android 12 and later). A restored phone that is not excluded gets a read failure
(handled as absent), not a wrong answer.

**Trimming and R8.** This preview ships no consumer ProGuard or R8 rule file. The sealed store takes its JSON type information
from the app's own source-generated context, which normally survives trimming. If a release build trims a type the app
serialises, keep that type in the app's rules.

**Certificates.** `AllowDangerousCertificates` belongs in the app's debug configuration only. Set it from the app's own
build, and do not set it in a release build.

**Ads start-up check.** In a release build, call `AdUnitIdSyntax.ReleaseConfigurationError` at start-up and log its message
when it is not `null`, so a missing or malformed id is visible in the logs rather than as silently absent ads.
