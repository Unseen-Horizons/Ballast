// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Http;

/// <summary>How the app's outbound HTTP clients are built.</summary>
public sealed record MauiHttpOptions
{
    /// <summary>
    /// How long a pooled connection is kept before its handler is rebuilt. Ten minutes keeps DNS fresh enough and the connections,
    /// and their TLS sessions, warm; the framework default of two minutes costs a fresh handshake every two minutes on mobile data.
    /// </summary>
    public TimeSpan HandlerLifetime { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Accepts any server certificate. Off by default. The app turns it on only from its own debug configuration, for a development
    /// server with a certificate the phone does not trust. A build that is not debuggable refuses it (see <see cref="DangerousCertificates"/>).
    /// </summary>
    public bool AllowDangerousCertificates { get; init; }
}
