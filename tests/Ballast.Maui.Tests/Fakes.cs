// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Serialization;

using Ballast.Maui.Ads;
using Ballast.Maui.Connectivity;
using Ballast.Maui.Diagnostics;
using Ballast.Maui.Helpers;
using Ballast.Maui.Http;
using Ballast.Maui.Permissions;
using Ballast.Maui.Platform;
using Ballast.Maui.Reviews;
using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>An in-memory <see cref="ISecureValues"/>. <see cref="ReadDelay"/> widens the "no key yet" window; <see cref="ReadFault"/> makes every read throw.</summary>
internal sealed class FakeSecureValues : ISecureValues
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);
    private int _writes;

    /// <summary>How long a read takes. Zero by default.</summary>
    public TimeSpan ReadDelay { get; set; }

    /// <summary>When set, every read throws it, as a restored phone's undecryptable value does.</summary>
    public Exception? ReadFault { get; set; }

    /// <summary>How many values were written.</summary>
    public int Writes => Volatile.Read(ref _writes);

    /// <summary>How many times <see cref="RemoveAll"/> ran.</summary>
    public int RemoveAllCalls { get; private set; }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (ReadDelay > TimeSpan.Zero)
            await Task.Delay(ReadDelay, ct);
        else
            await Task.Yield();

        if (ReadFault is not null)
            throw ReadFault;

        return _values.TryGetValue(key, out var value) ? value : null;
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _writes);
        _values[key] = value;
        return Task.CompletedTask;
    }

    public bool Remove(string key) => _values.TryRemove(key, out _);

    public void RemoveAll()
    {
        RemoveAllCalls++;
        _values.Clear();
    }

    /// <summary>The stored value, read without going through <see cref="GetAsync"/>.</summary>
    public string? Peek(string key) => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Clears values, counters, the delay and the fault.</summary>
    public void Reset()
    {
        _values.Clear();
        Volatile.Write(ref _writes, 0);
        ReadDelay = TimeSpan.Zero;
        ReadFault = null;
        RemoveAllCalls = 0;
    }
}

/// <summary>An in-memory <see cref="IPreferenceStore"/>, keyed by container and key. <see cref="Fault"/> makes every call throw.</summary>
internal sealed class FakePreferenceStore : IPreferenceStore
{
    private readonly ConcurrentDictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>When set, every call throws it.</summary>
    public Exception? Fault { get; set; }

    private static string Slot(string key, string? sharedName) => sharedName is null ? key : sharedName + "\u0001" + key;

    public T Get<T>(string key, T defaultValue, string? sharedName)
    {
        if (Fault is not null)
            throw Fault;
        return _values.TryGetValue(Slot(key, sharedName), out var value) && value is T typed ? typed : defaultValue;
    }

    public void Set<T>(string key, T value, string? sharedName)
    {
        if (Fault is not null)
            throw Fault;
        _values[Slot(key, sharedName)] = value;
    }

    public void Remove(string key, string? sharedName)
    {
        if (Fault is not null)
            throw Fault;
        _values.TryRemove(Slot(key, sharedName), out _);
    }

    /// <summary>The value stored under a key, or null.</summary>
    public object? Peek(string key, string? sharedName = null) => _values.TryGetValue(Slot(key, sharedName), out var value) ? value : null;
}

/// <summary>Paths under one temporary root: <c>data</c> for the data folder and <c>cache</c> for the cache folder.</summary>
internal sealed class FakeAppPaths : IAppPaths
{
    public FakeAppPaths(string root)
    {
        AppDataDirectory = Path.Combine(root, "data");
        CacheDirectory = Path.Combine(root, "cache");
    }

    public string AppDataDirectory { get; }

    public string CacheDirectory { get; }
}

/// <summary>A log that keeps every line it is given, for assertions.</summary>
internal sealed class RecordingLog : IClientLog
{
    public List<string> Warnings { get; } = [];

    public List<string> Failures { get; } = [];

    public void Failure(string source, string summary, string? code = null, string? detail = null, string? traceId = null, Exception? ex = null) =>
        Failures.Add($"{source}: {summary}");

    public void Warning(string source, string summary) => Warnings.Add($"{source}: {summary}");
}

/// <summary>A connectivity source the test drives.</summary>
internal sealed class FakeConnectivity : IConnectivitySource
{
    public bool Offline { get; set; }

    public bool Cellular { get; set; }

    public event EventHandler<bool>? Changed;

    /// <summary>How many listeners are subscribed right now.</summary>
    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    public bool IsOfflineNow() => Offline;

    public bool IsOnCellular() => Cellular;

    /// <summary>Changes the state and raises <see cref="Changed"/>.</summary>
    public void Raise(bool offline)
    {
        Offline = offline;
        Changed?.Invoke(this, offline);
    }
}

/// <summary>A package whose files are given by path. A missing path throws <see cref="FileNotFoundException"/>.</summary>
internal sealed class FakeAppPackage : IAppPackage
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>Calls to <see cref="OpenAsync"/>.</summary>
    public int Opens { get; private set; }

    /// <summary>Gives a file's text.</summary>
    public FakeAppPackage With(string path, string text)
    {
        _files[path] = text;
        return this;
    }

    public Task<Stream> OpenAsync(string path)
    {
        Opens++;
        if (!_files.TryGetValue(path, out var text))
            throw new FileNotFoundException("No such packaged file.", path);
        return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>Build and platform facts the test sets. <see cref="ReadFault"/> makes every value read throw.</summary>
internal sealed class FakeAppBuildInfo : IAppBuildInfo
{
    public string VersionString { get; set; } = "0.9.0";

    public string BuildString { get; set; } = "25";

    public string PlatformName { get; set; } = "Android";

    public string OsVersion { get; set; } = "16";

    /// <summary>When set, every read throws it.</summary>
    public Exception? ReadFault { get; set; }

    string IAppBuildInfo.VersionString => Read(() => VersionString);

    string IAppBuildInfo.BuildString => Read(() => BuildString);

    string IAppBuildInfo.PlatformName => Read(() => PlatformName);

    string IAppBuildInfo.OsVersion => Read(() => OsVersion);

    private string Read(Func<string> value)
    {
        if (ReadFault is not null)
            throw ReadFault;
        return value();
    }
}

/// <summary>A build that reports a fixed debuggable answer.</summary>
internal sealed record FakeRuntimeBuild(bool? IsDebuggable) : IRuntimeBuild;

/// <summary>A permission source the test drives. <see cref="Fault"/> makes every call throw.</summary>
internal sealed class FakePermissionSource : IPermissionSource
{
    /// <summary>What a check answers now.</summary>
    public AppPermissionStatus Status { get; set; } = AppPermissionStatus.Denied;

    /// <summary>What a request answers, after the prompt.</summary>
    public AppPermissionStatus StatusAfterRequest { get; set; } = AppPermissionStatus.Granted;

    /// <summary>Whether a rationale is shown.</summary>
    public bool Rationale { get; set; }

    /// <summary>When set, every call throws it.</summary>
    public Exception? Fault { get; set; }

    /// <summary>How many prompts were shown.</summary>
    public int Requests { get; private set; }

    public Task<AppPermissionStatus> CheckLocationAsync()
    {
        if (Fault is not null)
            throw Fault;
        return Task.FromResult(Status);
    }

    public Task<AppPermissionStatus> RequestLocationAsync()
    {
        if (Fault is not null)
            throw Fault;
        Requests++;
        Status = StatusAfterRequest;
        return Task.FromResult(Status);
    }

    public Task<bool> ShouldShowLocationRationaleAsync()
    {
        if (Fault is not null)
            throw Fault;
        return Task.FromResult(Rationale);
    }
}

/// <summary>A consent SDK the test drives. Raising an event is done by the test with the matching <c>Raise</c> method.</summary>
internal sealed class FakeAdConsent : IAdConsent
{
    /// <summary>What <see cref="CanRequestAds"/> answers.</summary>
    public bool CanRequest { get; set; }

    /// <summary>What <see cref="IsPrivacyOptionsRequired"/> answers.</summary>
    public bool PrivacyRequired { get; set; }

    /// <summary>When set, <see cref="CanRequestAds"/> and <see cref="IsPrivacyOptionsRequired"/> throw it.</summary>
    public Exception? Fault { get; set; }

    /// <summary>How many times the privacy answer was asked.</summary>
    public int PrivacyQueries { get; private set; }

    /// <summary>How many times the privacy form was shown.</summary>
    public int FormsShown { get; private set; }

    public event EventHandler<ConsentInfo>? InfoUpdated;

    public event EventHandler<string?>? InfoFailedToUpdate;

    public event EventHandler<string?>? FormError;

    public event EventHandler? FormDismissed;

    public bool CanRequestAds()
    {
        if (Fault is not null)
            throw Fault;
        return CanRequest;
    }

    public bool IsPrivacyOptionsRequired()
    {
        if (Fault is not null)
            throw Fault;
        PrivacyQueries++;
        return PrivacyRequired;
    }

    public void ShowPrivacyOptionsForm() => FormsShown++;

    public void RaiseInfoUpdated(ConsentInfo info) => InfoUpdated?.Invoke(this, info);

    public void RaiseInfoFailedToUpdate(string? message) => InfoFailedToUpdate?.Invoke(this, message);

    public void RaiseFormError(string? message) => FormError?.Invoke(this, message);

    public void RaiseFormDismissed() => FormDismissed?.Invoke(this, EventArgs.Empty);
}

/// <summary>A store rating prompt that counts its launches.</summary>
internal sealed class FakeStoreReviewPrompt : IStoreReviewPrompt
{
    /// <summary>What a launch answers.</summary>
    public bool Launches { get; set; } = true;

    /// <summary>How many times the prompt was launched.</summary>
    public int Asks { get; private set; }

    public Task<bool> RequestAsync()
    {
        Asks++;
        return Task.FromResult(Launches);
    }
}

/// <summary>A crash offer's screens that answer from a script. Each call takes the next answer, and an empty script answers Dismissed.</summary>
internal sealed class FakeCrashUi : ICrashReportUi
{
    private readonly Queue<CrashPromptAnswer> _answers = new();

    /// <summary>What the screens showed, in order.</summary>
    public List<string> Shown { get; } = [];

    /// <summary>Queues the next answer.</summary>
    public FakeCrashUi Answer(params CrashPromptAnswer[] answers)
    {
        foreach (var answer in answers)
            _answers.Enqueue(answer);
        return this;
    }

    public Task<CrashPromptAnswer> AskAsync()
    {
        Shown.Add("ask");
        return Task.FromResult(Next());
    }

    public Task<CrashPromptAnswer> ShowTextAsync(string reportText)
    {
        Shown.Add("text:" + reportText.Length);
        return Task.FromResult(Next());
    }

    public Task ShowSentAsync()
    {
        Shown.Add("sent");
        return Task.CompletedTask;
    }

    public Task ShowSendFailedAsync()
    {
        Shown.Add("failed");
        return Task.CompletedTask;
    }

    private CrashPromptAnswer Next() => _answers.Count > 0 ? _answers.Dequeue() : CrashPromptAnswer.Dismissed;
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan span) => _now = _now.Add(span);

    /// <summary>Sets the clock.</summary>
    public void Set(DateTimeOffset now) => _now = now;
}

/// <summary>A folder under the system temporary path, unique to one test, and deleted with <see cref="Dispose"/>.</summary>
internal sealed class TempRoot : IDisposable
{
    public TempRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ballast-maui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by a test: the system's temporary folder is cleared by the system in time.
        }
    }
}

/// <summary>A sample record for the sealed-store tests.</summary>
internal sealed record Sample(string Name, int Count);

/// <summary>The JSON context the sealed-store tests write and read with, as the apps do with their own.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Sample))]
internal sealed partial class TestJsonContext : JsonSerializerContext
{
}
