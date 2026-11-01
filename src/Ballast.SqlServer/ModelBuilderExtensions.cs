// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ballast.SqlServer;

/// <summary>
/// The two model conventions that an EF Core context applies to every entity in its model. Each one is a loop over the whole
/// model, so both are called from <c>OnModelCreating</c> at the same point: after the base configuration (and any
/// identity or AI tables), before the context configures its own entities. A relationship or property that is configured
/// after the call keeps EF Core's default instead of the convention.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Stores every enum property of the model as its name in text, nullable enums included. Each property gets an
    /// <c>EnumToStringConverter</c>, so the column holds, for example, <c>Pending</c>.
    /// </summary>
    /// <param name="modelBuilder">The model being built. Required.</param>
    /// <param name="maxLength">
    /// The maximum length of each enum column, in characters. Null (the default) leaves the length to EF Core, which gives
    /// <c>nvarchar(max)</c>. A value must be positive.
    /// </param>
    /// <returns><paramref name="modelBuilder"/>, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is zero or negative.</exception>
    public static ModelBuilder UseEnumsAsStrings(this ModelBuilder modelBuilder, int? maxLength = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (maxLength is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength,
                "A maximum length must be positive. Pass null to leave the length to EF Core (nvarchar(max)).");
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var type = property.ClrType;
                var underlying = Nullable.GetUnderlyingType(type) ?? type;

                if (underlying.IsEnum)
                {
                    var converterType = typeof(EnumToStringConverter<>).MakeGenericType(underlying);
                    var converter = (ValueConverter)Activator.CreateInstance(converterType)!;
                    property.SetValueConverter(converter);

                    if (maxLength is { } length)
                    {
                        property.SetMaxLength(length);
                    }
                }
            }
        }

        return modelBuilder;
    }

    /// <summary>
    /// Sets every foreign key that exists when it runs to <see cref="DeleteBehavior.Restrict"/>: a row that another row still
    /// refers to cannot be deleted, and nothing is deleted along with it. SQL Server refuses several cascade paths into one
    /// table, so a model with many relationships needs this. A relationship configured afterwards (with its own
    /// <c>HasOne</c>) is not changed here: it gets EF Core's default unless it sets <c>OnDelete</c> itself, so call this
    /// before the context's own relationship configuration.
    /// </summary>
    /// <param name="modelBuilder">The model being built. Required.</param>
    /// <returns><paramref name="modelBuilder"/>, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    public static ModelBuilder RestrictDeletes(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }

        return modelBuilder;
    }
}
