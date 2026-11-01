// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>The configuration sections <see cref="HostCredentials.Load"/> reads. Each app names its own.</summary>
public sealed class HostCredentialsOptions
{
    /// <summary>
    /// The indexed section: <c>{SectionName}:{n}:Host</c> and <c>{SectionName}:{n}:Credential</c>. The host is a value, so it may
    /// hold dots and hyphens, which an App Service setting name cannot. An environment variable is written
    /// <c>{SectionName with __}__{n}__Host</c>. Default <c>BasicAuthHosts</c>.
    /// </summary>
    public string SectionName { get; set; } = "BasicAuthHosts";

    /// <summary>
    /// The host-keyed section, optional: <c>{MapSectionName}:{host}</c> holds the credential, with the host as the key. Convenient in a
    /// user-secrets file. Null reads no such section. Default null.
    /// </summary>
    public string? MapSectionName { get; set; }
}
