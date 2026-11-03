// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>
/// The session token's own signing key and lifetime, and the sizes of its two in-memory stores. It is bound from the
/// configuration section named by <see cref="AppAttestationSetup.SessionSection"/> (<c>AppSession</c> by default).
/// <para>
/// The key is separate from the app's user-token key on purpose, so rotating one never signs out the other and a user token
/// can never validate as a session. Nothing about a session is stored: changing the key ends every session at once.
/// </para>
/// </summary>
public sealed class AppSessionOptions
{
    /// <summary>The lifetime of a session when the configuration does not set one: 24 hours.</summary>
    public const int DefaultLifetimeHours = 24;

    /// <summary>The longest lifetime a session may be given: 168 hours (one week). A value outside 1 to 168 stops the start.</summary>
    public const int MaximumLifetimeHours = 168;

    /// <summary>How many nonces or web tickets one in-memory store holds at once: 20,000.</summary>
    public const int DefaultStoreCapacity = 20_000;

    /// <summary>How long a nonce answers: five minutes.</summary>
    public static readonly TimeSpan DefaultNonceLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long a web ticket answers: sixty seconds.</summary>
    public static readonly TimeSpan DefaultTicketLifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The signing key, at least 32 bytes in UTF-8. Required: it comes from user secrets, an environment variable or a secret store,
    /// never from a file in the repository. Startup throws when it is missing, a placeholder (<c>CHANGE-ME</c>) or too short.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>How long a session is valid, in hours, from 1 to <see cref="MaximumLifetimeHours"/>. Default 24.</summary>
    public int LifetimeHours { get; set; } = DefaultLifetimeHours;

    /// <summary>How long a nonce answers. Must be longer than zero. Default five minutes.</summary>
    public TimeSpan NonceLifetime { get; set; } = DefaultNonceLifetime;

    /// <summary>How long a web ticket answers. Must be longer than zero. Default sixty seconds.</summary>
    public TimeSpan TicketLifetime { get; set; } = DefaultTicketLifetime;

    /// <summary>
    /// The most nonces, and separately the most web tickets, the in-memory store holds live at once. At least 1. Default 20,000.
    /// A full store refuses the next one with 503 rather than evicting a live entry.
    /// </summary>
    public int StoreCapacity { get; set; } = DefaultStoreCapacity;

    /// <summary>The session lifetime as a time span: <see cref="LifetimeHours"/> hours.</summary>
    public TimeSpan Lifetime => TimeSpan.FromHours(LifetimeHours);
}
