// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Microsoft.Extensions.Configuration;

namespace Ballast.AspNetCore;

/// <summary>The checks that stop an application from starting with a value that is not safe to run with.</summary>
public static class StartupChecks
{
    /// <summary>
    /// Reads a secret from the configuration and stops the start if it is missing, blank, one of the placeholder values, or shorter than
    /// <paramref name="minBytes"/> in UTF-8. The message names the key and says how to set it, with user secrets or with an environment
    /// variable, and never shows the value.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="key">The configuration key, such as <c>Jwt:Key</c>.</param>
    /// <param name="minBytes">The shortest the secret may be, in UTF-8 bytes. It must be at least 1.</param>
    /// <param name="placeholders">Values that are not secrets, such as <c>CHANGE-ME</c>. They match without case.</param>
    /// <returns>The secret, as it is configured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or only white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minBytes"/> is below 1.</exception>
    /// <exception cref="InvalidOperationException">The secret is not usable. The message names the key.</exception>
    public static string RequireSecret(IConfiguration configuration, string key, int minBytes, params string[] placeholders)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        ArgumentOutOfRangeException.ThrowIfLessThan(minBytes, 1, nameof(minBytes));

        var value = configuration[key];
        var how = $"Set it with user secrets (dotnet user-secrets set \"{key}\" \"<value>\") or with the environment variable {key.Replace(":", "__", StringComparison.Ordinal)}.";

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} must be configured. {how}");
        }

        if (Array.Exists(placeholders, placeholder => string.Equals(value, placeholder, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{key} still holds a placeholder value. {how}");
        }

        if (Encoding.UTF8.GetByteCount(value) < minBytes)
        {
            throw new InvalidOperationException($"{key} must be at least {minBytes} bytes long (UTF-8). {how}");
        }

        return value;
    }
}
