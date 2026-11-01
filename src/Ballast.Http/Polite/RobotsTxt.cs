// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Ballast.Http;

/// <summary>
/// One origin's robots.txt, read for one crawler under RFC 9309.
/// <list type="bullet">
/// <item><description>The group is the first one that names the crawler's product token (case-insensitive), else the first <c>*</c> group. Consecutive
/// <c>User-agent</c> lines share a group, and a <c>User-agent</c> line after a rule or a <c>Crawl-delay</c> line starts a new one.</description></item>
/// <item><description><c>Allow</c> and <c>Disallow</c> match by prefix, with <c>*</c> for any run of characters and <c>$</c> for the end of the path.
/// The longest matching rule wins; on a tie, <c>Allow</c> wins. A path no rule matches is allowed. An empty value is no rule.</description></item>
/// <item><description><c>Crawl-delay</c> (not standard, but common) is read from the same group, in seconds.</description></item>
/// </list>
/// A robots.txt that cannot be read is an answer too: a 4xx means everything is allowed, and a 5xx or a failure means everything is
/// disallowed. <see cref="Reason"/> says which, for the page that was refused.
/// </summary>
public sealed class RobotsTxt
{
    private readonly IReadOnlyList<Rule> _rules;
    private readonly bool _disallowsAll;

    private RobotsTxt(IReadOnlyList<Rule> rules, bool disallowsAll, TimeSpan? crawlDelay, string? reason)
    {
        _rules = rules;
        _disallowsAll = disallowsAll;
        CrawlDelay = crawlDelay;
        Reason = reason;
    }

    /// <summary>Allows every path, with no delay: robots.txt answered 4xx, or it has no group for this crawler.</summary>
    public static RobotsTxt AllowAll { get; } = new([], disallowsAll: false, crawlDelay: null, reason: null);

    /// <summary>Disallows every path: robots.txt answered 5xx, or it could not be read. <paramref name="reason"/> says why, in words.</summary>
    /// <param name="reason">The sentence that says why every path is disallowed.</param>
    /// <returns>The disallow-all answer.</returns>
    public static RobotsTxt DisallowAll(string reason) => new([], disallowsAll: true, crawlDelay: null, reason: reason);

    /// <summary>The Crawl-delay of this crawler's group, or null when there is none.</summary>
    public TimeSpan? CrawlDelay { get; }

    /// <summary>Why every path is allowed or disallowed, when the answer was not a parsed file. Null for a parsed file.</summary>
    public string? Reason { get; }

    /// <summary>
    /// The answer for a robots.txt fetched with <paramref name="status"/>: a 2xx is parsed for <paramref name="productToken"/>, a 4xx
    /// allows all, anything else disallows all.
    /// </summary>
    /// <param name="status">The HTTP status the robots.txt answered with.</param>
    /// <param name="body">The body, read as text.</param>
    /// <param name="productToken">The crawler's product token.</param>
    /// <returns>The answer.</returns>
    public static RobotsTxt FromStatus(int status, string body, string productToken)
    {
        if (status is >= 200 and < 300)
        {
            return Parse(body, productToken);
        }

        if (status is >= 400 and < 500)
        {
            return new([], disallowsAll: false, crawlDelay: null, reason: $"robots.txt answered {status}, so everything is allowed");
        }

        return DisallowAll($"robots.txt answered {status}, so everything is disallowed");
    }

    /// <summary>Reads a robots.txt body for one crawler.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="productToken">The crawler's product token, matched case-insensitively. Required.</param>
    /// <returns>The rules of the group that applies to the crawler.</returns>
    /// <exception cref="ArgumentException"><paramref name="productToken"/> is blank.</exception>
    public static RobotsTxt Parse(string text, string productToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(productToken);

        List<Group> groups = [];
        Group? current = null;
        foreach (var raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw;
            var comment = line.IndexOf('#', StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment];
            }
            line = line.Trim();

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (field)
            {
                case "user-agent":
                    if (current is null || current.HasDirectives)
                    {
                        current = new Group();
                        groups.Add(current);
                    }

                    current.Agents.Add(ProductOf(value));
                    break;

                case "allow":
                case "disallow":
                    if (current is null)
                    {
                        break;
                    }

                    current.HasDirectives = true;
                    if (value.Length > 0)
                    {
                        current.Rules.Add(new Rule(field == "allow", value));
                    }
                    break;

                case "crawl-delay":
                    if (current is null)
                    {
                        break;
                    }

                    current.HasDirectives = true;
                    if (current.CrawlDelay is null && TryParseSeconds(value, out var seconds))
                    {
                        current.CrawlDelay = seconds;
                    }
                    break;
            }
        }

        var group = groups.FirstOrDefault(g => g.Agents.Any(a => string.Equals(a, productToken, StringComparison.OrdinalIgnoreCase)))
            ?? groups.FirstOrDefault(g => g.Agents.Contains("*"));
        return group is null
            ? AllowAll
            : new(group.Rules, disallowsAll: false, crawlDelay: group.CrawlDelay, reason: null);
    }

    /// <summary>Whether <paramref name="pathAndQuery"/> (for example <c>/a/b?c=d</c>) may be fetched.</summary>
    /// <param name="pathAndQuery">The path and query of the page. Required.</param>
    /// <returns>True when no rule disallows it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pathAndQuery"/> is null.</exception>
    public bool IsAllowed(string pathAndQuery)
    {
        ArgumentNullException.ThrowIfNull(pathAndQuery);
        if (_disallowsAll)
        {
            return false;
        }

        var target = pathAndQuery.Length == 0 ? "/" : pathAndQuery;
        Rule? best = null;
        foreach (var rule in _rules)
        {
            if (!Matches(rule.Pattern, target))
            {
                continue;
            }

            if (best is null
                || rule.Pattern.Length > best.Pattern.Length
                || (rule.Pattern.Length == best.Pattern.Length && rule.Allow))
            {
                best = rule;
            }
        }

        return best is null || best.Allow;
    }

    /// <summary>A pattern against a path: <c>*</c> matches any run of characters, and a trailing <c>$</c> anchors the end.</summary>
    internal static bool Matches(string pattern, string path)
    {
        var anchored = pattern.EndsWith('$');
        var body = anchored ? pattern[..^1] : pattern;
        var parts = body.Split('*');
        if (!path.StartsWith(parts[0], StringComparison.Ordinal))
        {
            return false;
        }
        if (parts.Length == 1)
        {
            return !anchored || path.Length == parts[0].Length;
        }

        var position = parts[0].Length;
        for (var index = 1; index < parts.Length; index++)
        {
            var part = parts[index];
            if (index == parts.Length - 1 && anchored)
            {
                return path.Length - part.Length >= position && path.EndsWith(part, StringComparison.Ordinal);
            }

            if (part.Length == 0)
            {
                continue;
            }

            var found = path.IndexOf(part, position, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            position = found + part.Length;
        }

        return true;
    }

    /// <summary>The product token of a <c>User-agent</c> value: the part before any version or comment, trimmed.</summary>
    private static string ProductOf(string value)
    {
        var end = value.IndexOfAny(['/', ' ', '\t', '(']);
        var token = end >= 0 ? value[..end] : value;
        return token.Trim() == "*" ? "*" : token.Trim();
    }

    private static bool TryParseSeconds(string value, out TimeSpan seconds)
    {
        seconds = TimeSpan.Zero;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }
        if (parsed < 0 || parsed > TimeSpan.MaxValue.TotalSeconds)
        {
            return false;
        }

        seconds = TimeSpan.FromSeconds(parsed);
        return true;
    }

    private sealed class Group
    {
        public List<string> Agents { get; } = [];

        public List<Rule> Rules { get; } = [];

        public bool HasDirectives { get; set; }

        public TimeSpan? CrawlDelay { get; set; }
    }

    private sealed record Rule(bool Allow, string Pattern);
}
