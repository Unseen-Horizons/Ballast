// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Text;
using System.Diagnostics;
using System.Text.Json;

namespace Ballast.Maui.Auth;

/// <summary>
/// Reads the expiry of a JWT from its payload. The signature is not checked here: the server checks it on every call, and the
/// device only needs to know when to renew.
/// </summary>
public static class JwtExpiry
{
    /// <summary>The expiry (the <c>exp</c> claim), or null when the token has no readable one.</summary>
    public static DateTimeOffset? Read(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return null;

            // Base64url, padding optional: the BCL reads both, so no padding fix-up is needed.
            var payload = Base64Url.DecodeFromChars(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("exp", out var expProp) && expProp.TryGetInt64(out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds);

            return null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            // Null means no pre-emptive renewal: the call goes out with the token as it is and relies on the 401 path. Logged, not swallowed.
            Trace.WriteLine($"[JwtExpiry] Could not read token expiry: {ex.Message}");
            return null;
        }
    }

    /// <summary>True when the token ends at or before <paramref name="now"/> plus <paramref name="within"/>. False when it has no readable expiry.</summary>
    public static bool EndsWithin(string token, DateTimeOffset now, TimeSpan within) =>
        Read(token) is { } expiry && expiry <= now.Add(within);
}
