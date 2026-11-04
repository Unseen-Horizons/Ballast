// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Ballast.Health;

/// <summary>What this copy was built from, and when. Null means the build did not record it, never a guess.</summary>
/// <param name="Commit">The full commit sha in lowercase, or null.</param>
/// <param name="BuiltAtUtc">The build time as <c>yyyy-MM-ddTHH:mm:ssZ</c>, or null.</param>
public sealed record BuildInfo(string? Commit, string? BuiltAtUtc);

/// <summary>
/// Reads the build identity that the build writes into the assembly. The commit is the text after the first <c>+</c> of the
/// informational version (a deploy passes the commit as <c>SourceRevisionId</c>). The build time is an assembly metadata value
/// under a key the app names (<c>BuiltAtUtc</c> by default). A local build has neither, so both are null.
/// </summary>
public static class BuildInfoReader
{
    /// <summary>The metadata key the build time is read from when none is given: <c>BuiltAtUtc</c>.</summary>
    public const string DefaultBuiltAtKey = "BuiltAtUtc";

    private const string BuiltAtFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly Regex CommitPattern = new(@"\A[0-9a-f]{7,64}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the build identity of <paramref name="assembly"/>. A blank <paramref name="builtAtKey"/> is refused. Any other
    /// missing or unreadable value gives null for that field.
    /// </summary>
    /// <param name="assembly">The assembly to read. Required.</param>
    /// <param name="builtAtKey">The metadata key of the build time. Required, and not blank.</param>
    public static BuildInfo Read(Assembly assembly, string builtAtKey = DefaultBuiltAtKey)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(builtAtKey);

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var builtAt = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, builtAtKey, StringComparison.Ordinal))?.Value;
        return Parse(informational, builtAt);
    }

    /// <summary>The pure part of <see cref="Read(Assembly, string)"/>: the two raw values in, the build identity out.</summary>
    /// <param name="informationalVersion">The informational version text, or null.</param>
    /// <param name="builtAtRaw">The raw build time, or null.</param>
    public static BuildInfo Parse(string? informationalVersion, string? builtAtRaw) =>
        new(ParseCommit(informationalVersion), ParseBuiltAt(builtAtRaw));

    private static string? ParseCommit(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion))
        {
            return null;
        }

        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return null;
        }

        // Only a plain hex sha counts. Anything else (a suffix, a branch name, an empty value) is absent, so a reader
        // treats the commit as unknown rather than matching the wrong one.
        var candidate = informationalVersion[(plus + 1)..];
        return CommitPattern.IsMatch(candidate) ? candidate.ToLowerInvariant() : null;
    }

    private static string? ParseBuiltAt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
        {
            return null;
        }

        return utc.ToString(BuiltAtFormat, CultureInfo.InvariantCulture);
    }
}
