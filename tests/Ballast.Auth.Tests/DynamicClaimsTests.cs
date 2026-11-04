// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Auth.Tests;

/// <summary>The identity context the claims tests use. In memory, so no database server is needed.</summary>
public sealed class ClaimsDb : IdentityDbContext<StringUser, IdentityRole<string>, string>
{
    /// <summary>Creates the context.</summary>
    /// <param name="options">The options, with the in-memory store.</param>
    public ClaimsDb(DbContextOptions<ClaimsDb> options)
        : base(options)
    {
    }
}

/// <summary>
/// The claims transformation: the role and permission claims come from the store, the cache answers until it is evicted, and the eviction
/// calls reach the users that they must.
/// </summary>
public class DynamicClaimsTests
{
    private static ClaimsPrincipal SignedIn(string userId, params Claim[] extra) =>
        new(new ClaimsIdentity([new Claim("sub", userId), .. extra], "test"));

    private static List<string> ValuesOf(ClaimsPrincipal principal, string type) =>
        principal.FindAll(type).Select(c => c.Value).OrderBy(v => v, StringComparer.Ordinal).ToList();

    private static DynamicClaimsTransformation<StringUser, string, IdentityRole<string>> Transform(
        UserManager<StringUser> users,
        RoleManager<IdentityRole<string>> roles,
        IMemoryCache cache,
        DynamicClaimsOptions? options = null) =>
        new(users, roles, cache, options);

    private static async Task<string> NewUserAsync(UserManager<StringUser> users, string email)
    {
        var user = new StringUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        return user.Id;
    }

    private static async Task NewRoleAsync(RoleManager<IdentityRole<string>> roles, string name, params string[] permissions)
    {
        var role = new IdentityRole(name);
        Assert.True((await roles.CreateAsync(role)).Succeeded);
        foreach (var permission in permissions)
        {
            Assert.True((await roles.AddClaimAsync(role, new Claim("permission", permission))).Succeeded);
        }
    }

    private static async Task AddToRoleAsync(UserManager<StringUser> users, string userId, string roleName)
    {
        Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(userId))!, roleName)).Succeeded);
    }

    [Fact]
    public async Task Roles_and_permissions_come_from_the_store_and_replace_the_token_claims()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();

        var user = await NewUserAsync(users, "ana@example.com");
        await NewRoleAsync(roles, "Editor", "fares.read");
        await AddToRoleAsync(users, user, "Editor");
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var result = await Transform(users, roles, cache).TransformAsync(
            SignedIn(user, new Claim("role", "Ghost"), new Claim("permission", "stale.write")));

        Assert.Equal(["Editor"], ValuesOf(result, "role"));
        Assert.Equal(["fares.read"], ValuesOf(result, "permission"));
    }

    [Fact]
    public async Task A_role_change_is_seen_only_after_the_eviction_and_other_users_stay_cached()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();

        var holder = await NewUserAsync(users, "holder@example.com");
        var bystander = await NewUserAsync(users, "bystander@example.com");
        await NewRoleAsync(roles, "Editor", "old.read");
        await NewRoleAsync(roles, "Viewer", "other.read");
        await AddToRoleAsync(users, holder, "Editor");
        await AddToRoleAsync(users, bystander, "Viewer");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var transform = Transform(users, roles, cache);

        await transform.TransformAsync(SignedIn(holder));
        await transform.TransformAsync(SignedIn(bystander));
        Assert.True(cache.TryGetValue(DynamicClaimsCache.KeyFor(holder), out _));

        var editor = (await roles.FindByNameAsync("Editor"))!;
        Assert.True((await roles.RemoveClaimAsync(editor, new Claim("permission", "old.read"))).Succeeded);
        Assert.True((await roles.AddClaimAsync(editor, new Claim("permission", "new.read"))).Succeeded);

        // Until the eviction, the cached set answers: that is what the eviction is for.
        Assert.Equal(["old.read"], ValuesOf(await transform.TransformAsync(SignedIn(holder)), "permission"));

        await DynamicClaimsCache.EvictRoleMembersAsync<StringUser, string>(cache, users, "Editor");

        Assert.False(cache.TryGetValue(DynamicClaimsCache.KeyFor(holder), out _));
        Assert.True(cache.TryGetValue(DynamicClaimsCache.KeyFor(bystander), out _));
        Assert.Equal(["new.read"], ValuesOf(await transform.TransformAsync(SignedIn(holder)), "permission"));
        Assert.Equal(["other.read"], ValuesOf(await transform.TransformAsync(SignedIn(bystander)), "permission"));
    }

    [Fact]
    public async Task Evicting_one_user_shows_a_changed_membership_on_their_next_request()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();

        var user = await NewUserAsync(users, "ana@example.com");
        await NewRoleAsync(roles, "Editor", "fares.read");
        await AddToRoleAsync(users, user, "Editor");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var transform = Transform(users, roles, cache);

        Assert.Equal(["fares.read"], ValuesOf(await transform.TransformAsync(SignedIn(user)), "permission"));
        Assert.True((await users.RemoveFromRoleAsync((await users.FindByIdAsync(user))!, "Editor")).Succeeded);

        Assert.Equal(["fares.read"], ValuesOf(await transform.TransformAsync(SignedIn(user)), "permission"));

        DynamicClaimsCache.Evict(cache, user);
        var after = await transform.TransformAsync(SignedIn(user));

        Assert.Empty(ValuesOf(after, "role"));
        Assert.Empty(ValuesOf(after, "permission"));
    }

    [Fact]
    public async Task A_deleted_account_comes_back_unauthenticated_after_its_eviction()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();

        var user = await NewUserAsync(users, "gone@example.com");
        await NewRoleAsync(roles, "Editor", "fares.read");
        await AddToRoleAsync(users, user, "Editor");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var transform = Transform(users, roles, cache);
        Assert.True((await transform.TransformAsync(SignedIn(user))).Identity!.IsAuthenticated);

        Assert.True((await users.DeleteAsync((await users.FindByIdAsync(user))!)).Succeeded);
        DynamicClaimsCache.Evict(cache, user);

        var result = await transform.TransformAsync(SignedIn(user));
        Assert.False(result.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task A_principal_that_is_anonymous_or_has_no_subject_is_returned_unchanged()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var transform = Transform(users, roles, cache);

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.Same(anonymous, await transform.TransformAsync(anonymous));

        var noSubject = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Ghost")], "test"));
        var same = await transform.TransformAsync(noSubject);
        Assert.Same(noSubject, same);
        Assert.Equal(["Ghost"], ValuesOf(same, "role"));
    }

    [Fact]
    public async Task The_claim_names_and_the_permission_claim_type_come_from_the_options()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();

        var user = await NewUserAsync(users, "ana@example.com");
        var editor = new IdentityRole("Editor");
        Assert.True((await roles.CreateAsync(editor)).Succeeded);
        Assert.True((await roles.AddClaimAsync(editor, new Claim("perm", "fares.read"))).Succeeded);
        await AddToRoleAsync(users, user, "Editor");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = new DynamicClaimsOptions { SubjectClaimType = "uid", RoleClaimType = "roles", PermissionClaimType = "perm" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("uid", user), new Claim("roles", "Stale"), new Claim("permission", "unrelated")],
            "test"));

        var result = await Transform(users, roles, cache, options).TransformAsync(principal);

        Assert.Equal(["Editor"], ValuesOf(result, "roles"));
        Assert.Equal(["fares.read"], ValuesOf(result, "perm"));
        Assert.Equal(["unrelated"], ValuesOf(result, "permission"));
    }

    [Fact]
    public async Task A_size_limited_cache_accepts_the_entries_the_transformation_writes()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();

        var user = await NewUserAsync(users, "ana@example.com");
        await NewRoleAsync(roles, "Editor", "fares.read");
        await AddToRoleAsync(users, user, "Editor");
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1 });

        var result = await Transform(users, roles, cache).TransformAsync(SignedIn(user));

        Assert.Equal(["Editor"], ValuesOf(result, "role"));
    }

    [Fact]
    public void The_cache_key_is_the_prefix_and_the_user_id_and_eviction_needs_its_arguments()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(DynamicClaimsCache.KeyFor("abc"), "value");

        Assert.Equal("claims:abc", DynamicClaimsCache.KeyFor("abc"));
        Assert.Throws<ArgumentNullException>(() => DynamicClaimsCache.KeyFor(null!));
        Assert.Throws<ArgumentNullException>(() => DynamicClaimsCache.Evict(null!, "abc"));
        DynamicClaimsCache.Evict(cache, "abc");
        Assert.False(cache.TryGetValue(DynamicClaimsCache.KeyFor("abc"), out _));
    }

    [Fact]
    public void The_defaults_are_the_token_claim_names_and_the_library_cache_times()
    {
        var options = new DynamicClaimsOptions();

        Assert.Equal("sub", options.SubjectClaimType);
        Assert.Equal("role", options.RoleClaimType);
        Assert.Equal("permission", options.PermissionClaimType);
        Assert.Equal(TimeSpan.FromSeconds(30), options.SlidingExpiration);
        Assert.Equal(TimeSpan.FromMinutes(5), options.AbsoluteExpiration);
        options.Validate();
    }

    [Theory]
    [InlineData(nameof(DynamicClaimsOptions.SubjectClaimType))]
    [InlineData(nameof(DynamicClaimsOptions.RoleClaimType))]
    [InlineData(nameof(DynamicClaimsOptions.PermissionClaimType))]
    public void An_empty_claim_name_is_refused_and_named(string setting)
    {
        var options = new DynamicClaimsOptions();
        switch (setting)
        {
            case nameof(DynamicClaimsOptions.SubjectClaimType): options.SubjectClaimType = " "; break;
            case nameof(DynamicClaimsOptions.RoleClaimType): options.RoleClaimType = string.Empty; break;
            default: options.PermissionClaimType = " "; break;
        }

        var error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(setting, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cache_time_that_is_not_positive_or_a_sliding_time_above_the_absolute_one_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => new DynamicClaimsOptions { SlidingExpiration = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(() => new DynamicClaimsOptions { AbsoluteExpiration = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(() => new DynamicClaimsOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpiration = TimeSpan.FromMinutes(5),
        }.Validate());

        new DynamicClaimsOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5),
            AbsoluteExpiration = TimeSpan.FromMinutes(5),
        }.Validate();
    }

    [Fact]
    public async Task The_transformation_refuses_bad_options_when_it_is_built()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        Assert.Throws<InvalidOperationException>(() => Transform(users, roles, cache, new DynamicClaimsOptions { PermissionClaimType = string.Empty }));
    }

    [Fact]
    public async Task The_registration_adds_the_transformation_as_the_claims_transformation()
    {
        using var host = new ClaimsHost();
        await using var scope = host.Scope();

        var transform = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();

        Assert.IsType<DynamicClaimsTransformation<StringUser, string, IdentityRole<string>>>(transform);
    }

    [Fact]
    public void The_registration_refuses_bad_options_at_registration()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddBallastDynamicClaims<StringUser, string, IdentityRole<string>>(
            options => options.SlidingExpiration = TimeSpan.Zero));
    }

    /// <summary>A container with an in-memory identity store, the memory cache and the registration. Each test makes its own.</summary>
    private sealed class ClaimsHost : IDisposable
    {
        private readonly ServiceProvider _provider;

        public ClaimsHost()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ClaimsDb>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddIdentity<StringUser, IdentityRole<string>>().AddEntityFrameworkStores<ClaimsDb>();
            services.AddBallastDynamicClaims<StringUser, string, IdentityRole<string>>();
            _provider = services.BuildServiceProvider();
        }

        public AsyncServiceScope Scope() => _provider.CreateAsyncScope();

        public void Dispose() => _provider.Dispose();
    }
}
