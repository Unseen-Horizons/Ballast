// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Testing.Guards;

/// <summary>
/// The server-side half of a time-zone rule: a server reads no clock zone of its own, because it runs in UTC in the cloud and in the
/// owner's zone on a developer machine, and a business day has to be the same on both. The rule's C# patterns are here; each app
/// keeps its own list of folders to scan and its own reviewed exceptions, and names the places where a local clock is really an event
/// time.
/// </summary>
public static class ClockGuard
{
    /// <summary>
    /// The C# reads of the machine's own clock zone: <c>DateTime.Now</c>, <c>DateTime.Today</c>, <c>DateTimeOffset.Now</c>,
    /// <c>DateTimeOffset.Today</c>, <c>ToLocalTime()</c> and <c>TimeZoneInfo.Local</c>.
    /// </summary>
    public static readonly Regex ServerClockPattern = new(
        @"\bDateTime\.Now\b|\bDateTime\.Today\b|\bDateTimeOffset\.Now\b|\bDateTimeOffset\.Today\b|\.ToLocalTime\(\)|\bTimeZoneInfo\.Local\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The findings in one C# source: one line per match, as <c>path:line: code</c>. Comments are removed first, so a banned name in a
    /// comment is not a finding, and the line numbers are the source's own.
    /// </summary>
    /// <param name="relativePath">The file's path, as it appears in the findings. Required.</param>
    /// <param name="source">The file's text. Required.</param>
    /// <param name="banned">The pattern to find. Default: <see cref="ServerClockPattern"/>.</param>
    public static IReadOnlyList<string> Findings(string relativePath, string source, Regex? banned = null)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(source);
        var pattern = banned ?? ServerClockPattern;
        var lines = SourceScan.StripCSharpComments(source).Split('\n');
        List<string> findings = [];
        for (var i = 0; i < lines.Length; i++)
        {
            if (pattern.IsMatch(lines[i]))
            {
                findings.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");
            }
        }

        return findings;
    }
}
