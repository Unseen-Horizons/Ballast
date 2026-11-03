// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth;

/// <summary>Maps the e-mail link table the sign-in library reads and writes.</summary>
public static class AuthEmailLinkModel
{
    /// <summary>The table name the library uses, which is the default.</summary>
    public const string DefaultTableName = "AuthEmailLinks";

    /// <summary>The longest subject key a link may carry.</summary>
    public const int SubjectKeyMaxLength = 128;

    /// <summary>The longest purpose a link may carry.</summary>
    public const int PurposeMaxLength = 64;

    /// <summary>The length the hash column is given. The hash is 44 characters; 128 leaves room, and keeps the unique index legal.</summary>
    public const int TokenHashMaxLength = 128;

    /// <summary>
    /// Maps the <see cref="AuthEmailLink"/> entity: its table, its key, the lengths of its text columns, a unique index on the hash that a
    /// redeem looks up, and an index for the rate-limit count. Call it from the app's <c>OnModelCreating</c>, and add a
    /// <c>DbSet&lt;AuthEmailLink&gt;</c> to the context.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="tableName">The table name. Default <c>AuthEmailLinks</c>.</param>
    /// <returns>The same model builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is null, empty or blank.</exception>
    public static ModelBuilder ApplyEmailLinkModel(this ModelBuilder modelBuilder, string tableName = DefaultTableName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<AuthEmailLink>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey(link => link.Id);

            entity.Property(link => link.SubjectKey).HasMaxLength(SubjectKeyMaxLength).IsRequired();
            entity.Property(link => link.Purpose).HasMaxLength(PurposeMaxLength).IsRequired();
            entity.Property(link => link.TokenHash).HasMaxLength(TokenHashMaxLength).IsRequired();

            // Unique: a redeem finds its link by the hash, and two rows with one hash would make the single use ambiguous.
            entity.HasIndex(link => link.TokenHash).IsUnique();

            // The rate-limit count filters on subject, purpose and the issue time, so this index keeps it a seek.
            entity.HasIndex(link => new { link.SubjectKey, link.Purpose, link.CreatedAt });
        });

        return modelBuilder;
    }
}
