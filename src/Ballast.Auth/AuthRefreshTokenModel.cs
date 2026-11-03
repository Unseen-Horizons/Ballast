// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth;

/// <summary>Maps the refresh token table the sign-in library reads and writes.</summary>
public static class AuthRefreshTokenModel
{
    /// <summary>The default table name.</summary>
    public const string DefaultTableName = "RefreshTokens";

    /// <summary>
    /// Maps a refresh token entity: its table, its key, the lengths of the hash and the unused columns, a unique index on the hash, and a
    /// required foreign key to the user. Call it from the app's <c>OnModelCreating</c>, once per refresh token entity.
    /// </summary>
    /// <typeparam name="TEntity">The app's refresh token entity, which derives from <see cref="AuthRefreshTokenBase{TKey,TUser}"/>.</typeparam>
    /// <typeparam name="TKey">The user key type.</typeparam>
    /// <typeparam name="TUser">The app's user type.</typeparam>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="tableName">The table name. Default <c>RefreshTokens</c>.</param>
    /// <param name="onUserDelete">What deleting a user does to its tokens. Default <see cref="DeleteBehavior.Restrict"/>, which
    /// blocks the user's delete while tokens exist. Pass <see cref="DeleteBehavior.Cascade"/> to delete the tokens with the user.</param>
    /// <returns>The same model builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is null, empty or blank.</exception>
    public static ModelBuilder ApplyRefreshTokenModel<TEntity, TKey, TUser>(
        this ModelBuilder modelBuilder,
        string tableName = DefaultTableName,
        DeleteBehavior onUserDelete = DeleteBehavior.Restrict)
        where TEntity : AuthRefreshTokenBase<TKey, TUser>
        where TKey : IEquatable<TKey>
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<TEntity>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey(rt => rt.Id);

            // The hash is 44 characters; 128 leaves room. Unbounded, it would be nvarchar(max), which SQL Server cannot index, and the
            // unique index below would never be created.
            entity.Property(rt => rt.Token).HasMaxLength(128);
            entity.Property(rt => rt.ReplacedByToken).HasMaxLength(128);
            entity.Property(rt => rt.DeviceInfo).HasMaxLength(256);
            entity.Property(rt => rt.IpAddress).HasMaxLength(64);

            // Unique: the hash is the lookup key at a refresh, and two rows sharing one hash would make rotation and reuse detection ambiguous.
            entity.HasIndex(rt => rt.Token).IsUnique();

            entity.HasOne(rt => rt.User)
                .WithMany()
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(onUserDelete);
        });

        return modelBuilder;
    }
}
