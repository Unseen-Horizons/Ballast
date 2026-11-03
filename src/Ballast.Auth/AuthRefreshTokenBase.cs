// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The columns of a refresh token row. An app's own refresh token entity derives from it and adds nothing, so the table is the one the
/// library maps (<see cref="AuthRefreshTokenModel.ApplyRefreshTokenModel{TEntity,TKey,TUser}"/>). An app keeps its own class name,
/// which keeps its existing model snapshot unchanged.
/// </summary>
/// <typeparam name="TKey">The user key type: a string or an int.</typeparam>
/// <typeparam name="TUser">The app's user type.</typeparam>
/// <remarks>
/// <see cref="DeviceInfo"/> and <see cref="IpAddress"/> are no longer written. They stay as columns, unused, because a column drop is a
/// later release's job (the expand and contract rule). An app may clear old values with its own script.
/// </remarks>
public abstract class AuthRefreshTokenBase<TKey, TUser>
    where TKey : IEquatable<TKey>
    where TUser : class
{
    /// <summary>The row's id.</summary>
    public int Id { get; set; }

    /// <summary>The stored hash of the raw token: base64 SHA-256, 44 characters. Unique.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The user the token belongs to.</summary>
    public TKey UserId { get; set; } = default!;

    /// <summary>The user the token belongs to, loaded with the row when it is needed.</summary>
    public TUser User { get; set; } = default!;

    /// <summary>When the token expires, in UTC.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When the token was created, in UTC. A remember-me token is recognised by its lifetime from this time.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the token was revoked, in UTC, or null while it is live.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>The hash of the token that replaced this one when it was rotated, or null. Its presence means the token was used once already.</summary>
    public string? ReplacedByToken { get; set; }

    /// <summary>Not written any more. The column stays, unused.</summary>
    public string? DeviceInfo { get; set; }

    /// <summary>Not written any more. The column stays, unused.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Whether the token is past its expiry. Computed; not a column.</summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>Whether the token has been revoked. Computed; not a column.</summary>
    public bool IsRevoked => RevokedAt.HasValue;

    /// <summary>Whether the token is neither expired nor revoked. Computed; not a column, so queries spell the condition out.</summary>
    public bool IsActive => !IsExpired && !IsRevoked;
}
