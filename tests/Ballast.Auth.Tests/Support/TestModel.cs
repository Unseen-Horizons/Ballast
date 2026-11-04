// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth.Tests;

/// <summary>A user with a string (GUID text) key, as GetThereAPI has.</summary>
public sealed class StringUser : IdentityUser, IAuthProfile
{
    /// <inheritdoc />
    public string FullName { get; set; } = string.Empty;
}

/// <summary>A user with an int key, as Chistify's Identity tables will have.</summary>
public sealed class IntUser : IdentityUser<int>, IAuthProfile
{
    /// <inheritdoc />
    public string FullName { get; set; } = string.Empty;
}

/// <summary>A refresh token row for a string-keyed user. The app's own entity would look like this.</summary>
public sealed class StringRefresh : AuthRefreshTokenBase<string, StringUser>
{
}

/// <summary>A refresh token row for an int-keyed user.</summary>
public sealed class IntRefresh : AuthRefreshTokenBase<int, IntUser>
{
}

/// <summary>
/// An int-keyed user whose key the app sets itself, from its own profile table, and the database does not generate. This is how Chistify maps
/// its Identity users (AspNetUsers.Id has no identity).
/// </summary>
public sealed class ManualKeyUser : IdentityUser<int>, IAuthProfile
{
    /// <inheritdoc />
    public string FullName { get; set; } = string.Empty;
}

/// <summary>A refresh token row for the app-keyed user.</summary>
public sealed class ManualKeyRefresh : AuthRefreshTokenBase<int, ManualKeyUser>
{
}

/// <summary>
/// The test database context. It holds Identity, the refresh tokens (mapped by the library) and the audit rows. The refresh token entity is
/// a parameter, so the same context serves both key types.
/// </summary>
/// <typeparam name="TUser">The user.</typeparam>
/// <typeparam name="TKey">The key.</typeparam>
/// <typeparam name="TRefresh">The refresh token entity.</typeparam>
public sealed class AuthTestDb<TUser, TKey, TRefresh> : IdentityDbContext<TUser, IdentityRole<TKey>, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>
{
    /// <summary>Creates the context.</summary>
    /// <param name="options">The options, with the connection.</param>
    public AuthTestDb(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>The refresh tokens.</summary>
    public DbSet<TRefresh> RefreshTokens { get; set; } = default!;

    /// <summary>The audit rows.</summary>
    public DbSet<AuditRow> AuditRows { get; set; } = default!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // The app sets this user's key itself, so the database must not generate it.
        if (typeof(TUser) == typeof(ManualKeyUser))
        {
            builder.Entity<ManualKeyUser>().Property(user => user.Id).ValueGeneratedNever();
        }

        builder.ApplyRefreshTokenModel<TRefresh, TKey, TUser>();
    }
}
