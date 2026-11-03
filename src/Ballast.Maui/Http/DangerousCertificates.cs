// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Http;

/// <summary>
/// The handler that accepts any server certificate, for a developer's local server with a certificate the phone does not trust.
/// </summary>
/// <remarks>
/// <para>
/// It is turned on only when the app asks for it, through <see cref="MauiHttpOptions.AllowDangerousCertificates"/>. The app sets that
/// from its own debug build configuration; the library never reads a build symbol itself, so a release build has the option off
/// unless the app's code turns it on.
/// </para>
/// <para>
/// A build that reports itself as not debuggable refuses it: <see cref="CreateHandler"/> throws, so a mistaken setting fails at
/// start-up rather than shipping an app that trusts every certificate.
/// </para>
/// </remarks>
public static class DangerousCertificates
{
    /// <summary>
    /// The primary handler for a client: the system's own trust when <paramref name="allowed"/> is false, otherwise one that accepts
    /// every certificate.
    /// </summary>
    /// <param name="allowed">Whether the app asked for certificates to be accepted unchecked.</param>
    /// <param name="build">Whether this build is debuggable. Only consulted when <paramref name="allowed"/> is true.</param>
    /// <returns>The handler. The caller owns it.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="allowed"/> is true and the build reports itself as not debuggable.</exception>
    public static HttpMessageHandler CreateHandler(bool allowed, IRuntimeBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (!allowed)
            return new HttpClientHandler();

        if (build.IsDebuggable is false)
            throw new InvalidOperationException(
                "Certificate checks may be turned off only in a debuggable build. This build is not debuggable, so the setting is refused.");

        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
    }
}
