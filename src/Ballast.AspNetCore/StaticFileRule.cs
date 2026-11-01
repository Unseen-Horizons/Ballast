// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;

namespace Ballast.AspNetCore;

/// <summary>
/// A rule for the headers of a static file: its content security policy, its cache control and any other header, chosen by its
/// path and, optionally, by the end of the path. Every rule that matches a file applies, in the order the rules were added, and a
/// later rule overwrites the header an earlier one set. A file that no rule matches keeps the default content security policy.
/// </summary>
public sealed class StaticFileRule
{
    /// <summary>
    /// Creates a rule for a path.
    /// </summary>
    /// <param name="path">
    /// The path the rule applies to, starting with '/'. A rule for <c>/</c> applies to every file. Unless <see cref="ExactPath"/>
    /// is set, the rule applies to the path and to everything under it, by segment, so <c>/admin</c> covers <c>/admin/x.js</c> but not
    /// <c>/administrator</c>. The comparison ignores case.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, blank or does not start with '/'.</exception>
    public StaticFileRule(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));
        if (!path.StartsWith('/'))
        {
            throw new ArgumentException($"The static file rule path '{path}' must start with '/'.", nameof(path));
        }

        Path = path;
    }

    /// <summary>The path the rule applies to, starting with '/'.</summary>
    public string Path { get; }

    /// <summary>
    /// True when the rule applies to exactly <see cref="Path"/> and not to the files under it. The default is false.
    /// </summary>
    public bool ExactPath { get; init; }

    /// <summary>
    /// The file endings the request path must end with, such as <c>.html</c> or <c>.html.br</c>, compared without case. When it is empty,
    /// the rule applies to any ending. A path that ends with none of them does not match.
    /// </summary>
    public IList<string> Suffixes { get; } = [];

    /// <summary>
    /// The <c>Content-Security-Policy</c> header to write, or null to leave the header as it is.
    /// </summary>
    public string? ContentSecurityPolicy { get; init; }

    /// <summary>
    /// The <c>Cache-Control</c> header to write, or null to leave the header as it is.
    /// </summary>
    public string? CacheControl { get; init; }

    /// <summary>
    /// Any other headers to write, by name, overwriting what is there. A name must not be blank. The comparison ignores case.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the rule applies to a request path.
    /// </summary>
    /// <param name="requestPath">The path of the request.</param>
    /// <returns>True when the rule applies to the file at this path.</returns>
    internal bool Matches(PathString requestPath)
    {
        var pathMatches = ExactPath
            ? string.Equals(requestPath.Value, Path, StringComparison.OrdinalIgnoreCase)
            : Path == "/" || requestPath.StartsWithSegments(Path);
        if (!pathMatches)
        {
            return false;
        }

        if (Suffixes.Count == 0)
        {
            return true;
        }

        var value = requestPath.Value ?? string.Empty;
        return Suffixes.Any(suffix => value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
