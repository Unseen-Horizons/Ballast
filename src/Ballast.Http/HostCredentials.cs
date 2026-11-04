// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;

namespace Ballast.Http;

/// <summary>
/// The basic-auth credentials per host, read from configuration and never from a database or from a URL. A feed whose host
/// answers <c>401 WWW-Authenticate: Basic</c> needs one. Two shapes are read, and they mean the same thing: the indexed list
/// (<see cref="HostCredentialsOptions.SectionName"/>), and the host-keyed map (<see cref="HostCredentialsOptions.MapSectionName"/>).
/// </summary>
/// <remarks>
/// The indexed list exists because App Service on Linux allows only letters, numbers and underscores in an application setting's
/// name, and a host has dots and usually hyphens, so the host moves into a value, where that restriction does not apply. When both
/// shapes name one host, the indexed list wins: it is the one an operator sets on a deployment on purpose.
/// </remarks>
public static class HostCredentials
{
    /// <summary>
    /// Reads both shapes into one host-keyed map. Host comparison is case-insensitive, because the host is matched against the
    /// URL's host, which keeps the casing it was written in.
    /// </summary>
    /// <param name="configuration">The configuration to read. Required.</param>
    /// <param name="options">The section names. Required.</param>
    /// <returns>The credential for each host, keyed case-insensitively. Empty when neither section is set.</returns>
    /// <exception cref="ArgumentException"><see cref="HostCredentialsOptions.SectionName"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// A map entry has no credential, or an indexed entry is half-filled (a host without a credential, or the reverse). Both are
    /// refused at load, so a typo is a startup failure rather than an anonymous fetch that fails with a bare 401.
    /// </exception>
    public static IReadOnlyDictionary<string, string> Load(IConfiguration configuration, HostCredentialsOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SectionName);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.MapSectionName))
        {
            foreach (var child in configuration.GetSection(options.MapSectionName).GetChildren())
            {
                // A host listed with no credential would load as an empty string, which a caller reads as "no credential":
                // the request would then be sent anonymously and fail with a bare 401.
                if (string.IsNullOrWhiteSpace(child.Value))
                {
                    throw new InvalidOperationException(
                        $"{options.MapSectionName} entry '{child.Key}' has no credential. Set it to 'username:password' or remove the entry.");
                }

                result[child.Key] = child.Value;
            }
        }

        foreach (var entry in configuration.GetSection(options.SectionName).GetChildren())
        {
            var host = entry["Host"];
            var credential = entry["Credential"];

            var hasHost = !string.IsNullOrWhiteSpace(host);
            var hasCredential = !string.IsNullOrWhiteSpace(credential);
            if (hasHost != hasCredential)
            {
                throw new InvalidOperationException(
                    $"{options.SectionName} entry '{entry.Key}' is half-filled: Host='{host ?? string.Empty}' Credential={(hasCredential ? "present" : "missing")}. " +
                    "Both must be set or both absent: a typo in Host or a missing Credential is a deploy bug.");
            }

            if (!hasHost && !hasCredential)
            {
                continue;
            }

            result[host!] = credential!;
        }

        return result;
    }
}
