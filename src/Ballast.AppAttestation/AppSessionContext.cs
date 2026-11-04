// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;

using Ballast.AspNetCore;
using Ballast.Errors;

using Microsoft.AspNetCore.Http;

namespace Ballast.AppAttestation;

/// <summary>Reading a session off a request, the rate-limit partition that follows from it, and the 401 that a refused call answers.</summary>
public static class AppSessionContext
{
    private const string ItemKey = "ballast.app-attestation.session";

    // Random per process and never persisted: what a log line shows cannot be reversed by hashing every IPv4 address (an unkeyed
    // 12-hex hash of an address could be), and it changes on every restart.
    private static readonly byte[] LogKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>The session header, else the session cookie, else null. A blank value counts as absent.</summary>
    /// <param name="request">The request.</param>
    /// <param name="setup">The setup, which names the header and the cookie.</param>
    /// <returns>The token, trimmed, or null.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public static string? ReadToken(HttpRequest request, AppAttestationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(setup);

        if (request.Headers.TryGetValue(setup.HeaderName, out var header) && !string.IsNullOrWhiteSpace(header.ToString()))
            return header.ToString().Trim();

        return request.Cookies.TryGetValue(setup.CookieName, out var cookie) && !string.IsNullOrWhiteSpace(cookie)
            ? cookie.Trim()
            : null;
    }

    /// <summary>The session the gate found on the request, or null when there is none.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The session and its token, or null.</returns>
    public static AppSessionFeature? Get(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ItemKey, out var value) ? value as AppSessionFeature : null;
    }

    internal static void Set(HttpContext context, AppSessionFeature session) => context.Items[ItemKey] = session;

    /// <summary>
    /// The rate-limit partition: the app session when there is one, else the signed-in user, else the client address. Two installs
    /// behind one NAT address get two buckets; a session id is per session.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>The partition key. It is never null.</returns>
    public static string RateLimitKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Get(context) is { } session)
            return $"app:{session.Info.SessionId}";

        var userId = context.User.FindFirst("sub")?.Value;
        return userId is not null
            ? $"user:{userId}"
            : $"ip:{ClientAddressKey.For(context.Connection.RemoteIpAddress)}";
    }

    /// <summary>
    /// What a log line says about a rate-limit partition instead of the partition itself, which can be a raw client address: the kind
    /// (<c>ip</c>, <c>user</c>, <c>app</c>) and a 12-hex keyed SHA-256 prefix. The same caller reads the same within one process run,
    /// so "how many different callers" can still be answered, but no address, user id or session id is written to a log.
    /// </summary>
    /// <param name="partition">The partition key.</param>
    /// <returns>The label, for example <c>ip:3f9a0c1d2b4e</c>.</returns>
    public static string LogLabel(string partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        var colon = partition.IndexOf(':', StringComparison.Ordinal);
        var kind = colon > 0 ? partition[..colon] : "partition";
        var hash = HMACSHA256.HashData(LogKey, Encoding.UTF8.GetBytes(partition));
        return $"{kind}:{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    /// <summary>Writes the 401 a refused data call answers, with the setup's title, detail and code.</summary>
    /// <param name="context">The request to refuse.</param>
    /// <param name="setup">The setup, which holds the answer's words.</param>
    /// <returns>A task that completes when the body is written.</returns>
    public static async Task WriteRequiredAsync(HttpContext context, AppAttestationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(setup);

        var problem = new Problem
        {
            Type = ProblemTypes.For(StatusCodes.Status401Unauthorized),
            Title = setup.SessionRequiredTitle,
            Status = StatusCodes.Status401Unauthorized,
            Detail = setup.SessionRequiredDetail,
            Instance = context.Request.Path.HasValue ? context.Request.Path.Value : null,
            Code = setup.SessionRequiredCode,
            TraceId = ProblemResponses.TraceIdOf(context),
        };
        await ProblemResponses.WriteAsync(context, problem, context.RequestAborted);
    }
}
