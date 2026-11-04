// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballast.Auth.Tests;

/// <summary>The test configuration: a valid key, issuer and audience, and nothing else.</summary>
internal static class TestConfig
{
    /// <summary>A signing key of 48 characters, which is above the 32-byte minimum.</summary>
    public const string Key = "test-signing-key-for-ballast-auth-tests-only";

    /// <summary>The issuer the tests use.</summary>
    public const string Issuer = "ballast-auth-tests";

    /// <summary>The audience the tests use.</summary>
    public const string Audience = "ballast-auth-tests-clients";

    /// <summary>A key as the text a claim carries, invariant, as the library writes it.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>Its text.</returns>
    public static string KeyText<TKey>(TKey key) => Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>A configuration with the three required token settings, plus any extra pairs.</summary>
    /// <param name="extra">More settings, as key and value pairs.</param>
    /// <returns>The configuration.</returns>
    public static IConfiguration Build(params KeyValuePair<string, string?>[] extra)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Key,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
        };

        foreach (var pair in extra)
        {
            values[pair.Key] = pair.Value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}

/// <summary>
/// A host for one test: a fresh database, the library registered over the test context, a manual clock, and a hash counter. Each test
/// makes its own, and disposing it drops the database.
/// </summary>
/// <typeparam name="TUser">The user.</typeparam>
/// <typeparam name="TKey">The key.</typeparam>
/// <typeparam name="TRefresh">The refresh token entity.</typeparam>
internal sealed class Harness<TUser, TKey, TRefresh> : IAsyncDisposable
    where TUser : IdentityUser<TKey>, IAuthProfile, new()
    where TKey : IEquatable<TKey>
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
{
    private Harness(SqlTestDatabase database, ServiceProvider services, ManualTimeProvider time, HashCounter hashes)
    {
        Database = database;
        Services = services;
        Time = time;
        Hashes = hashes;
    }

    /// <summary>The database for this test.</summary>
    public SqlTestDatabase Database { get; }

    /// <summary>The container with the library registered.</summary>
    public ServiceProvider Services { get; }

    /// <summary>The clock. Move it to test expiry.</summary>
    public ManualTimeProvider Time { get; }

    /// <summary>The hash counter, for the timing tests.</summary>
    public HashCounter Hashes { get; }

    /// <summary>Creates a host. Skips the test when SQL Server cannot be reached.</summary>
    /// <param name="configuration">The configuration. Defaults to <see cref="TestConfig.Build()"/>.</param>
    /// <param name="configure">Sets the options after they are read.</param>
    /// <param name="extra">Adds more services before the library, such as a test hasher.</param>
    /// <param name="interceptor">A command interceptor for the test context, so a test can read the SQL it sends. Null for none.</param>
    /// <returns>The host, with an empty database.</returns>
    public static async Task<Harness<TUser, TKey, TRefresh>> CreateAsync(
        IConfiguration? configuration = null,
        Action<AuthOptions>? configure = null,
        Action<IServiceCollection>? extra = null,
        DbCommandInterceptor? interceptor = null)
    {
        var database = await SqlTestDatabase.CreateAsync();
        var time = new ManualTimeProvider();
        var hashes = new HashCounter();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(hashes);
        services.AddDbContext<AuthTestDb<TUser, TKey, TRefresh>>(o =>
        {
            o.UseSqlServer(database.ConnectionString);
            if (interceptor is not null)
            {
                o.AddInterceptors(interceptor);
            }
        });
        services.AddScoped<IAuthAudit>(sp => new AuditSink<AuthTestDb<TUser, TKey, TRefresh>>(sp.GetRequiredService<AuthTestDb<TUser, TKey, TRefresh>>()));
        extra?.Invoke(services);

        services.AddBallastAuth<TUser, TKey, IdentityRole<TKey>, AuthTestDb<TUser, TKey, TRefresh>, TRefresh>(
            configuration ?? TestConfig.Build(),
            configure);

        // Registered after the library, so this counting hasher is the one Identity gets. It wraps the library's own hasher, so the
        // behaviour is the real one and only the count is added.
        services.AddScoped<IPasswordHasher<TUser>>(sp => new CountingHasher<TUser>(
            sp.GetRequiredService<HashCounter>(),
            new AuthPasswordHasher<TUser>(sp.GetRequiredService<AuthOptions>(), sp.GetRequiredService<IOptions<PasswordHasherOptions>>())));

        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthTestDb<TUser, TKey, TRefresh>>().Database.EnsureCreatedAsync();
        }

        return new Harness<TUser, TKey, TRefresh>(database, provider, time, hashes);
    }

    /// <summary>A scope with its own database context, as one request has.</summary>
    /// <returns>The scope, which must be disposed.</returns>
    public AsyncServiceScope Scope() => Services.CreateAsyncScope();

    /// <summary>Creates a user with a password, as the registration does.</summary>
    /// <param name="email">The address, which is also the user name.</param>
    /// <param name="password">The password.</param>
    /// <param name="fullName">The display name.</param>
    /// <returns>The user's id, as the key type.</returns>
    public async Task<TKey> CreateUserAsync(string email, string? password, string fullName = "Ana Test")
    {
        await using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        var user = new TUser { UserName = email, Email = email, FullName = fullName };
        var result = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Test user not created: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return user.Id;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await Database.DisposeAsync();
    }
}
