// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ballast.SqlServer.Tests;

/// <summary>
/// Proves that <see cref="ModelBuilderExtensions.UseEnumsAsStrings"/> and <see cref="ModelBuilderExtensions.RestrictDeletes"/> give
/// the same model as the loops the two APIs run today. The loops are copied into the contexts below, verbatim apart from the class
/// names, and each model is described property by property and foreign key by foreign key.
/// </summary>
public class ModelConventionTests
{
    // ── the loops, as the two APIs write them today ─────────────────

    /// <summary>GetThereAPI/Data/AppDbContext.cs: enums as strings with no length, every foreign key restricted.</summary>
    private sealed class GetThereLoopContext : DbContext
    {
        public GetThereLoopContext(DbContextOptions<GetThereLoopContext> options) : base(options) { }

        public DbSet<Owner> Owners => Set<Owner>();
        public DbSet<Thing> Things => Set<Thing>();
        public DbSet<Part> Parts => Set<Part>();
        public DbSet<Tag> Tags => Set<Tag>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
                    }
                }

                foreach (var fk in entityType.GetForeignKeys())
                {
                    fk.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }
        }
    }

    /// <summary>TransitInfoAPI/Data/TransitDbContext.cs: enums as strings with a length of 50, every foreign key restricted.</summary>
    private sealed class TransitInfoLoopContext : DbContext
    {
        public TransitInfoLoopContext(DbContextOptions<TransitInfoLoopContext> options) : base(options) { }

        public DbSet<Owner> Owners => Set<Owner>();
        public DbSet<Thing> Things => Set<Thing>();
        public DbSet<Part> Parts => Set<Part>();
        public DbSet<Tag> Tags => Set<Tag>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
                        property.SetMaxLength(50);
                    }
                }

                // Disable cascade deletes globally — SQL Server doesn't allow multiple cascade paths
                foreach (var fk in entityType.GetForeignKeys())
                {
                    fk.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }
        }
    }

    // ── the same models with the library ────────────────────────────

    private sealed class LibraryUnboundedContext : DbContext
    {
        public LibraryUnboundedContext(DbContextOptions<LibraryUnboundedContext> options) : base(options) { }

        public DbSet<Owner> Owners => Set<Owner>();
        public DbSet<Thing> Things => Set<Thing>();
        public DbSet<Part> Parts => Set<Part>();
        public DbSet<Tag> Tags => Set<Tag>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseEnumsAsStrings();
            modelBuilder.RestrictDeletes();
        }
    }

    private sealed class LibraryFiftyContext : DbContext
    {
        public LibraryFiftyContext(DbContextOptions<LibraryFiftyContext> options) : base(options) { }

        public DbSet<Owner> Owners => Set<Owner>();
        public DbSet<Thing> Things => Set<Thing>();
        public DbSet<Part> Parts => Set<Part>();
        public DbSet<Tag> Tags => Set<Tag>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseEnumsAsStrings(50);
            modelBuilder.RestrictDeletes();
        }
    }

    // ── the model, described ────────────────────────────────────────

    /// <summary>One line per property and per foreign key: the provider type, column type, length, nullability, converter and delete rule.</summary>
    private static List<string> Describe(IModel model)
    {
        List<string> lines = [];
        foreach (var entityType in model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            foreach (var property in entityType.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var converter = property.GetValueConverter() is { } c
                    ? $"{c.GetType().Name}<{c.ModelClrType.Name}>"
                    : "none";
                var maxLength = property.GetMaxLength() is { } length ? length.ToString(CultureInfo.InvariantCulture) : "-";
                // The type the provider stores: the converter's provider type when there is one, else the property's own type.
                var providerType = property.GetValueConverter()?.ProviderClrType ?? property.ClrType;
                lines.Add(
                    $"{entityType.ShortName()}.{property.Name}: clr={TypeName(property.ClrType)} provider={TypeName(providerType)} "
                    + $"column={property.GetColumnType()} maxLength={maxLength} nullable={property.IsNullable} converter={converter}");
            }

            foreach (var foreignKey in entityType.GetForeignKeys()
                .OrderBy(f => f.PrincipalEntityType.ShortName(), StringComparer.Ordinal)
                .ThenBy(f => string.Join(",", f.Properties.Select(p => p.Name)), StringComparer.Ordinal))
            {
                var columns = string.Join(",", foreignKey.Properties.Select(p => p.Name));
                lines.Add($"{entityType.ShortName()}({columns}) -> {foreignKey.PrincipalEntityType.ShortName()}: delete={foreignKey.DeleteBehavior}");
            }
        }
        return lines;
    }

    /// <summary>The short name of a type, with a question mark for a nullable value type (Colour?).</summary>
    private static string TypeName(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    private static DbContextOptions<T> Options<T>() where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlServer("Server=unused.invalid;Database=unused;Trusted_Connection=True").Options;

    // ── the tests ───────────────────────────────────────────────────

    [Fact]
    public void UseEnumsAsStrings_and_RestrictDeletes_give_the_model_GetThereAPI_builds_today()
    {
        var copied = new GetThereLoopContext(Options<GetThereLoopContext>()).Model;
        var library = new LibraryUnboundedContext(Options<LibraryUnboundedContext>()).Model;

        Assert.Equal(Describe(copied), Describe(library));
    }

    [Fact]
    public void UseEnumsAsStrings_with_50_and_RestrictDeletes_give_the_model_TransitInfoAPI_builds_today()
    {
        var copied = new TransitInfoLoopContext(Options<TransitInfoLoopContext>()).Model;
        var library = new LibraryFiftyContext(Options<LibraryFiftyContext>()).Model;

        Assert.Equal(Describe(copied), Describe(library));
    }

    [Fact]
    public void The_column_types_and_nullability_are_the_ones_the_apps_produce()
    {
        var lines = Describe(new LibraryUnboundedContext(Options<LibraryUnboundedContext>()).Model);

        // A required enum is a NOT NULL text column with no length, and a nullable enum is a nullable one.
        Assert.Contains("Thing.Colour: clr=Colour provider=String column=nvarchar(max) maxLength=- nullable=False converter=EnumToStringConverter`1<Colour>", lines);
        Assert.Contains("Thing.Paint: clr=Colour? provider=String column=nvarchar(max) maxLength=- nullable=True converter=EnumToStringConverter`1<Colour>", lines);
        Assert.Contains("Part.Finish: clr=Size provider=String column=nvarchar(max) maxLength=- nullable=False converter=EnumToStringConverter`1<Size>", lines);
    }

    [Fact]
    public void A_length_of_50_is_applied_to_every_enum_column_and_no_other()
    {
        var lines = Describe(new LibraryFiftyContext(Options<LibraryFiftyContext>()).Model);

        Assert.Contains("Thing.Colour: clr=Colour provider=String column=nvarchar(50) maxLength=50 nullable=False converter=EnumToStringConverter`1<Colour>", lines);
        Assert.Contains("Thing.Paint: clr=Colour? provider=String column=nvarchar(50) maxLength=50 nullable=True converter=EnumToStringConverter`1<Colour>", lines);
        Assert.Contains("Thing.Name: clr=String provider=String column=nvarchar(max) maxLength=- nullable=False converter=none", lines);
    }

    [Fact]
    public void Every_foreign_key_is_restricted_including_the_one_of_a_many_to_many_join()
    {
        var lines = Describe(new LibraryUnboundedContext(Options<LibraryUnboundedContext>()).Model);

        Assert.Contains(lines, line => line.StartsWith("Part(ThingId) -> Thing:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("Thing(OwnerId) -> Owner:", StringComparison.Ordinal));
        Assert.All(lines.Where(line => line.Contains("delete=", StringComparison.Ordinal)), line => Assert.EndsWith("delete=Restrict", line));
        Assert.True(lines.Count(line => line.Contains("delete=", StringComparison.Ordinal)) >= 4, "the owner, part and join relationships are all described");
    }

    [Fact]
    public void Properties_that_are_not_enums_keep_their_own_type()
    {
        var lines = Describe(new LibraryUnboundedContext(Options<LibraryUnboundedContext>()).Model);

        Assert.DoesNotContain(lines, line => line.StartsWith("Owner.Name:", StringComparison.Ordinal) && line.Contains("converter=EnumToStringConverter", StringComparison.Ordinal));
        Assert.Contains("Owner.Id: clr=Int32 provider=Int32 column=int maxLength=- nullable=False converter=none", lines);
    }

    [Fact]
    public void UseEnumsAsStrings_refuses_a_length_that_is_not_positive()
    {
        var builder = new ModelBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseEnumsAsStrings(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseEnumsAsStrings(-1));
    }

    [Fact]
    public void The_conventions_refuse_a_null_model_builder()
    {
        Assert.Throws<ArgumentNullException>(() => ModelBuilderExtensions.UseEnumsAsStrings(null!));
        Assert.Throws<ArgumentNullException>(() => ModelBuilderExtensions.RestrictDeletes(null!));
    }

    // ── the entities ────────────────────────────────────────────────

    public enum Colour
    {
        Red,
        Green,
    }

    public enum Size
    {
        Small,
        Large,
    }

    public sealed class Owner
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public sealed class Thing
    {
        public int Id { get; set; }

        public Colour Colour { get; set; }

        public Colour? Paint { get; set; }

        public Size Option { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? OwnerId { get; set; }

        public Owner? Owner { get; set; }

        public List<Part> Parts { get; set; } = [];

        public List<Tag> Tags { get; set; } = [];
    }

    public sealed class Part
    {
        public int Id { get; set; }

        public int ThingId { get; set; }

        public Thing? Thing { get; set; }

        public Size Finish { get; set; }
    }

    public sealed class Tag
    {
        public int Id { get; set; }

        public List<Thing> Things { get; set; } = [];
    }
}
