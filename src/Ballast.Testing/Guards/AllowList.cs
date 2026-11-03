// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Testing.Guards;

/// <summary>A reviewed exception in an allow-list: the key of what it excuses, and the reason it is allowed.</summary>
/// <param name="Key">What the entry excuses. The guard decides what a key is, such as a file and a rule.</param>
/// <param name="Reason">Why the exception is acceptable. Required, and long enough to say something.</param>
public sealed record AllowEntry(string Key, string Reason);

/// <summary>
/// The rules every allow-list in a guard follows. An entry excuses one finding with the same key, and only when it has a reason of
/// at least the minimum length, is listed once, and still matches a finding. An entry that breaks any of these excuses nothing, so
/// the finding it was meant to cover fails too. A stale entry, one that matches no finding, fails, so an allow-list shrinks as the
/// code is fixed rather than growing silently.
/// </summary>
public static class AllowList
{
    /// <summary>The shortest reason an entry may give: 20 characters.</summary>
    public const int DefaultMinimumReasonLength = 20;

    /// <summary>
    /// The problems with an allow-list and its findings. Empty when every finding is excused by a valid entry and every entry is valid
    /// and still needed.
    /// </summary>
    /// <param name="listName">The list's name, which starts each message, such as <c>MY-GUARD-ALLOW-LIST</c>.</param>
    /// <param name="entries">The entries. Required.</param>
    /// <param name="findings">The findings, each with the key an entry would use to excuse it. Required.</param>
    /// <param name="minimumReasonLength">The shortest reason an entry may give.</param>
    /// <returns>One message per problem, in the order: invalid entries, stale entries, then findings no entry excuses.</returns>
    public static IReadOnlyList<string> Problems(
        string listName,
        IEnumerable<AllowEntry> entries,
        IEnumerable<string> findings,
        int minimumReasonLength = DefaultMinimumReasonLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listName);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(findings);
        if (minimumReasonLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumReasonLength), "The minimum reason length is at least 1.");
        }

        var problems = new List<string>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var valid = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                problems.Add($"{listName}: an entry has no key, so it excuses nothing. Remove it or name the finding.");
                continue;
            }

            if (!listed.Add(entry.Key))
            {
                problems.Add($"{listName} {entry.Key}: listed more than once. Keep one entry.");
                continue;
            }

            if (entry.Reason is null || entry.Reason.Trim().Length < minimumReasonLength)
            {
                problems.Add($"{listName} {entry.Key}: the reason must be at least {minimumReasonLength} characters.");
                continue;
            }

            valid.Add(entry.Key);
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        var unexcused = new List<string>();
        foreach (var finding in findings)
        {
            found.Add(finding);
            if (!valid.Contains(finding))
            {
                unexcused.Add(finding);
            }
        }

        foreach (var key in listed.Where(key => !found.Contains(key)))
        {
            problems.Add($"{listName} {key}: stale, nothing it excuses is found. Remove the entry.");
        }

        foreach (var finding in unexcused)
        {
            problems.Add($"{listName}: {finding} is not excused by a reviewed entry with a reason of at least {minimumReasonLength} characters.");
        }

        return problems;
    }
}
