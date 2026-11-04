// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AspNetCore;

/// <summary>
/// The security headers of every response, and the rules for the headers of static files. Set once, in
/// <see cref="SecurityHeaderServiceExtensions.AddSecurityHeaders"/>.
/// </summary>
public sealed class SecurityHeaderOptions
{
    /// <summary>
    /// The <c>Referrer-Policy</c> header. The default is <c>no-referrer</c>.
    /// </summary>
    public string ReferrerPolicy { get; set; } = "no-referrer";

    /// <summary>
    /// The <c>X-Frame-Options</c> header. The default is <c>DENY</c>.
    /// </summary>
    public string FrameOptions { get; set; } = "DENY";

    /// <summary>
    /// The <c>Permissions-Policy</c> header, or null to write none. The default turns the camera, the microphone and the location off:
    /// <c>camera=(), microphone=(), geolocation=()</c>.
    /// </summary>
    public string? PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=()";

    /// <summary>
    /// The <c>Content-Security-Policy</c> header of every response that does not set its own, including every static file whose rules set
    /// none. The default allows nothing: <c>default-src 'none'; frame-ancestors 'none'; base-uri 'self'</c>. It suits an API, which
    /// returns data and no page.
    /// </summary>
    public string DefaultContentSecurityPolicy { get; set; } = "default-src 'none'; frame-ancestors 'none'; base-uri 'self'";

    /// <summary>
    /// The rules for the headers of static files, in the order they are applied. Empty by default, so every static file keeps the
    /// default content security policy and no cache header.
    /// </summary>
    public IList<StaticFileRule> StaticFileRules { get; } = [];
}
