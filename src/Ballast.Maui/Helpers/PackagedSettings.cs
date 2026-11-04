// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

using Ballast.Maui.Diagnostics;

namespace Ballast.Maui.Helpers;

/// <summary>Opens a file that is packaged with the app, read-only.</summary>
public interface IAppPackage
{
    /// <summary>Opens the packaged file.</summary>
    /// <param name="path">The file's path inside the package, for example <c>appsettings.json</c>.</param>
    /// <returns>A stream of the file's bytes. The caller disposes it.</returns>
    /// <exception cref="FileNotFoundException">The package has no such file.</exception>
    Task<Stream> OpenAsync(string path);
}

/// <summary>
/// The packaged settings file, read once and shared by every reader: the backend addresses, the sign-in ids and the ads switch each read a part of
/// it. The document is held for the life of the app and never disposed, because giving its pooled buffers back would invalidate every reader.
/// </summary>
/// <remarks>
/// A missing or malformed file is logged and yields a null document. Every reader then falls back to its compiled-in default, so an unreadable
/// settings file never stops the app from starting.
/// </remarks>
public sealed class PackagedSettings
{
    private readonly Lazy<JsonDocument?> _shared;

    /// <summary>Creates the reader. Nothing is read until <see cref="Document"/> is first used.</summary>
    /// <param name="package">Where the file is packaged.</param>
    /// <param name="fileName">The file's name in the package. The default is <c>appsettings.json</c>.</param>
    /// <param name="log">Where a file that cannot be read is logged.</param>
    public PackagedSettings(IAppPackage package, string fileName, IClientLog log)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(log);
        _shared = new Lazy<JsonDocument?>(() => Read(package, fileName, log), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The parsed settings, or null when the file is missing or malformed.</summary>
    public JsonDocument? Document => _shared.Value;

    private static JsonDocument? Read(IAppPackage package, string fileName, IClientLog log)
    {
        try
        {
            using var stream = package.OpenAsync(fileName).GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            return JsonDocument.Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or JsonException or FileNotFoundException)
        {
            log.Warning(nameof(PackagedSettings), $"could not read {fileName}; every reader falls back to its default ({ex.GetType().Name})");
            return null;
        }
    }
}
