// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ballast.Auth.Tests;

/// <summary>
/// The refresh token table, read from the EF model. No database is needed: the model is built, and its mapping is compared with the
/// GetThereAPI mapping the library reproduces (its table, lengths, index, unused columns and foreign key).
/// </summary>
public class RefreshTokenModelTests
{
    /// <summary>A context that maps only the refresh token table, with a chosen table name and delete rule.</summary>
    private sealed class ModelOnlyContext<TUser, TKey, TRefresh> : DbContext
        where TUser : class
        where TKey : IEquatable<TKey>
        where TRefresh : AuthRefreshTokenBase<TKey, TUser>
    {
        private readonly string _table;
        private readonly DeleteBehavior _onDelete;

        public ModelOnlyContext(DbContextOptions options, string table, DeleteBehavior onDelete)
            : base(options)
        {
            _table = table;
            _onDelete = onDelete;
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<TUser>();
            builder.ApplyRefreshTokenModel<TRefresh, TKey, TUser>(_table, _onDelete);
        }
    }

    private static DbContextOptions InMemory() => new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    [Fact]
    public void The_table_is_refresh_tokens_with_the_lengths_get_there_api_has()
    {
        using var db = new AuthTestDb<StringUser, string, StringRefresh>(InMemory());
        var entity = db.Model.FindEntityType(typeof(StringRefresh))!;

        Assert.Equal("RefreshTokens", entity.GetTableName());
        Assert.Equal(128, entity.FindProperty(nameof(AuthRefreshTokenBase<string, StringUser>.Token))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(AuthRefreshTokenBase<string, StringUser>.ReplacedByToken))!.GetMaxLength());
        Assert.Equal(256, entity.FindProperty(nameof(AuthRefreshTokenBase<string, StringUser>.DeviceInfo))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(AuthRefreshTokenBase<string, StringUser>.IpAddress))!.GetMaxLength());
    }

    [Fact]
    public void The_hash_column_is_unique_and_the_unused_columns_are_still_mapped()
    {
        using var db = new AuthTestDb<StringUser, string, StringRefresh>(InMemory());
        var entity = db.Model.FindEntityType(typeof(StringRefresh))!;

        var tokenIndex = entity.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(AuthRefreshTokenBase<string, StringUser>.Token));
        Assert.True(tokenIndex.IsUnique);
        Assert.NotNull(entity.FindProperty(nameof(AuthRefreshTokenBase<string, StringUser>.IpAddress)));
        Assert.NotNull(entity.FindProperty(nameof(AuthRefreshTokenBase<string, StringUser>.DeviceInfo)));
    }

    [Fact]
    public void The_user_foreign_key_restricts_deletes_by_default()
    {
        using var db = new AuthTestDb<StringUser, string, StringRefresh>(InMemory());
        var foreignKey = db.Model.FindEntityType(typeof(StringRefresh))!.GetForeignKeys().Single();

        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal(typeof(StringUser), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(AuthRefreshTokenBase<string, StringUser>.UserId), foreignKey.Properties.Single().Name);
        Assert.True(foreignKey.IsRequired);
    }

    [Fact]
    public void A_cascade_and_another_table_name_are_options()
    {
        using var db = new ModelOnlyContext<StringUser, string, StringRefresh>(InMemory(), "Sessions", DeleteBehavior.Cascade);
        var entity = db.Model.FindEntityType(typeof(StringRefresh))!;

        Assert.Equal("Sessions", entity.GetTableName());
        Assert.Equal(DeleteBehavior.Cascade, entity.GetForeignKeys().Single().DeleteBehavior);
    }

    [Fact]
    public void An_int_keyed_user_maps_an_int_foreign_key()
    {
        using var db = new ModelOnlyContext<IntUser, int, IntRefresh>(InMemory(), "RefreshTokens", DeleteBehavior.Restrict);
        var foreignKey = db.Model.FindEntityType(typeof(IntRefresh))!.GetForeignKeys().Single();

        Assert.Equal(typeof(int), foreignKey.Properties.Single().ClrType);
    }

    [Fact]
    public void A_blank_table_name_is_refused_before_anything_is_mapped()
    {
        var builder = new ModelBuilder();

        Assert.Throws<ArgumentException>(() => builder.ApplyRefreshTokenModel<StringRefresh, string, StringUser>(" "));
    }
}
