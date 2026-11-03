// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

using Ballast.Errors;
using Ballast.Jobs;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Auth.Tests;

using MsSignIn = MicrosoftSignInService<StringUser, string, IdentityRole<string>, StringRefresh, AuthTestDb<StringUser, string, StringRefresh>>;
using MsSync = MicrosoftDirectorySync<StringUser, string, IdentityRole<string>, StringRefresh, AuthTestDb<StringUser, string, StringRefresh>>;
using StringDb = AuthTestDb<StringUser, string, StringRefresh>;

/// <summary>A directory that answers from a fixed snapshot, and counts the reads, so the tests need no network.</summary>
internal sealed class FakeGraph : IMicrosoftGraphClient
{
    /// <summary>The snapshot the next read returns.</summary>
    public MicrosoftDirectorySnapshot Snapshot { get; set; } = new([], [], new Dictionary<Guid, MicrosoftUser>(), null);

    /// <summary>How many reads were made.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    public Task<MicrosoftDirectorySnapshot> ReadDirectoryAsync(CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(Snapshot);
    }
}

/// <summary>The app's emergency accounts, which a test fills in once the accounts exist.</summary>
internal sealed class FakeEmergency : IMicrosoftEmergencyAccounts<string>
{
    /// <summary>The ids of the emergency accounts.</summary>
    public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> IdsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(Ids, StringComparer.Ordinal));
}

/// <summary>The handshake cookie of one request, faked: the principal it reads (none is a failed handshake), and the schemes it was removed for.</summary>
internal sealed class FakeHandshake : IAuthenticationService
{
    /// <summary>The principal the handshake produced, or null for a failed handshake.</summary>
    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>The schemes whose cookie was removed, in order.</summary>
    public List<string?> SignedOutSchemes { get; } = [];

    /// <inheritdoc />
    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
        Task.FromResult(Principal is null
            ? AuthenticateResult.Fail("The handshake did not complete.")
            : AuthenticateResult.Success(new AuthenticationTicket(Principal, "test")));

    /// <inheritdoc />
    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
    {
        SignedOutSchemes.Add(scheme);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
        throw new NotSupportedException();
}

/// <summary>Microsoft staff sign-in on SQL Server: the one-time codes, the recheck at the exchange, the refusals and the directory sync's stop.</summary>
public class MicrosoftSignInTests
{
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string Client = "66666666-7777-8888-9999-000000000000";
    private const string Password = "Correct-horse-42!";
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bo = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid AdminRole = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Two_exchanges_of_one_code_race_and_exactly_one_wins()
    {
        await using var host = await HostAsync();
        var userId = await MicrosoftAccountAsync(host, "ana@example.test", Ana);
        var code = await IssueAsync(host, userId);

        var outcomes = await Task.WhenAll(ExchangeOutcomeAsync(host, code), ExchangeOutcomeAsync(host, code));

        Assert.Equal(1, outcomes.Count(o => o == "ok"));
        Assert.Equal(1, outcomes.Count(o => o == MicrosoftCodeInvalid));
    }

    [Fact]
    public async Task A_valid_code_gives_one_session_and_cannot_be_used_again()
    {
        await using var host = await HostAsync();
        var userId = await MicrosoftAccountAsync(host, "ana@example.test", Ana);
        var code = await IssueAsync(host, userId);

        await using (var scope = host.Scope())
        {
            var session = await Service(scope).ExchangeAsync(code);
            Assert.False(string.IsNullOrEmpty(session.AccessToken));
            Assert.False(string.IsNullOrEmpty(session.RefreshToken));
        }

        Assert.Equal(MicrosoftCodeInvalid, await ExchangeOutcomeAsync(host, code));
    }

    [Fact]
    public async Task An_account_switched_off_after_its_code_was_issued_is_refused_at_the_exchange()
    {
        await using var host = await HostAsync();
        var userId = await MicrosoftAccountAsync(host, "ana@example.test", Ana);
        var code = await IssueAsync(host, userId);

        // The sync switches the account off between the handshake and the exchange: the exchange rechecks it.
        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            var user = await users.FindByIdAsync(userId);
            await users.SetLockoutEnabledAsync(user!, true);
            await users.SetLockoutEndDateAsync(user!, DateTimeOffset.MaxValue);
        }

        Assert.Equal(MicrosoftCodeInvalid, await ExchangeOutcomeAsync(host, code));
    }

    [Fact]
    public async Task A_code_expires_after_sixty_seconds()
    {
        await using var host = await HostAsync();
        var userId = await MicrosoftAccountAsync(host, "ana@example.test", Ana);
        var code = await IssueAsync(host, userId);

        host.Time.Now = host.Time.Now.Add(MsSignIn.CodeLifetime).AddSeconds(1);

        Assert.Equal(MicrosoftCodeInvalid, await ExchangeOutcomeAsync(host, code));
    }

    [Fact]
    public async Task A_first_sign_in_creates_the_account_with_its_roles_and_a_later_one_signs_into_it()
    {
        await using var host = await HostAsync(configure: o => o.FullAccessRole = "Admin");

        await using (var scope = host.Scope())
        {
            var outcome = await Service(scope).CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"]));
            Assert.True(outcome.IsAccepted);
        }

        await using (var scope = host.Scope())
        {
            var outcome = await Service(scope).CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"]));
            Assert.True(outcome.IsAccepted);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            var user = await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D"));
            Assert.NotNull(user);
            Assert.Contains("Admin", await users.GetRolesAsync(user!));
        }
    }

    [Fact]
    public async Task A_password_account_with_the_same_address_is_refused_and_never_linked()
    {
        await using var host = await HostAsync(configure: o => o.FullAccessRole = "Admin");
        await host.CreateUserAsync("ana@example.test", Password);

        await using var scope = host.Scope();
        var outcome = await Service(scope).CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"]));

        Assert.Equal(MicrosoftRefusal.AddressTaken, outcome.RefusalCode);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        Assert.Null(await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D")));
    }

    [Fact]
    public async Task An_address_at_another_company_domain_is_refused_before_the_account_is_written()
    {
        await using var host = await HostAsync(configure: o => o.AllowedDomains = ["example.test"]);

        await using var scope = host.Scope();
        var outcome = await Service(scope).CompleteAsync(Principal("ana@other.test", Ana, ["Api.Admin"]));

        Assert.Equal(MicrosoftRefusal.NotCompanyAddress, outcome.RefusalCode);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        Assert.Null(await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D")));
    }

    [Fact]
    public async Task A_sync_that_would_switch_off_every_linked_account_writes_nothing_and_says_so()
    {
        var graph = new FakeGraph();
        await using var host = await HostAsync(graph: graph, sync: true);
        var userId = await MicrosoftAccountAsync(host, "ana@example.test", Ana);

        // An empty directory: the run would switch the only linked account off.
        graph.Snapshot = new MicrosoftDirectorySnapshot(
            [new MicrosoftAppRole(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Api.Admin")],
            [],
            new Dictionary<Guid, MicrosoftUser>(),
            null);

        await using var scope = host.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.LooksWrong, result.Outcome);
        Assert.Equal(1, graph.Reads);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var user = await users.FindByIdAsync(userId);
        Assert.False(await users.IsLockedOutAsync(user!));
    }

    [Fact]
    public async Task A_sync_that_cannot_read_the_directory_writes_nothing()
    {
        var graph = new FakeGraph();
        await using var host = await HostAsync(graph: graph, sync: true);
        var userId = await MicrosoftAccountAsync(host, "ana@example.test", Ana);
        var failing = new ThrowingGraph();

        await using var scope = host.Scope();
        var sync = new MicrosoftDirectorySync<StringUser, string, IdentityRole<string>, StringRefresh, StringDb>(
            scope.ServiceProvider.GetRequiredService<MicrosoftSignInOptions>(),
            failing,
            new AlwaysHolderLeadership(),
            scope.ServiceProvider.GetRequiredService<StringDb>(),
            scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>(),
            scope.ServiceProvider.GetRequiredService<AuthSessionService<StringUser, string, StringRefresh, StringDb>>(),
            scope.ServiceProvider.GetRequiredService<IAuthAudit>(),
            scope.ServiceProvider.GetRequiredService<IMicrosoftEmergencyAccounts<string>>(),
            host.Time,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MsSync>>());

        var result = await sync.RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.ReadFailed, result.Outcome);
        Assert.Equal(DirectorySyncFailure.Graph, result.Failure);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        Assert.False(await users.IsLockedOutAsync((await users.FindByIdAsync(userId))!));
    }

    [Fact]
    public async Task A_failed_handshake_redirects_with_the_refusal_and_removes_the_handshake_cookie()
    {
        await using var host = await HostAsync();
        var handshake = new FakeHandshake();

        await using var scope = host.Scope();
        var location = await Service(scope).CompleteAsync(Context(handshake));

        Assert.Equal("/admin/login.html?error=MS_SIGNIN_FAILED", location);
        Assert.Equal(new List<string?> { "MicrosoftSignIn.Cookie" }, handshake.SignedOutSchemes);
    }

    [Fact]
    public async Task A_completed_handshake_redirects_with_a_one_time_code_that_exchanges_for_a_session()
    {
        await using var host = await HostAsync(configure: o => o.FullAccessRole = "Admin");
        var handshake = new FakeHandshake { Principal = Principal("ana@example.test", Ana, ["Api.Admin"]) };

        string location;
        await using (var scope = host.Scope())
        {
            location = await Service(scope).CompleteAsync(Context(handshake));
        }

        Assert.StartsWith("/admin/login.html?code=", location, StringComparison.Ordinal);
        Assert.Equal(new List<string?> { "MicrosoftSignIn.Cookie" }, handshake.SignedOutSchemes);
        Assert.Equal("ok", await ExchangeOutcomeAsync(host, CodeFrom(location)));
    }

    [Fact]
    public async Task A_sign_in_never_links_or_changes_an_emergency_account()
    {
        var emergency = new FakeEmergency();
        await using var host = await HostAsync(configure: o => o.FullAccessRole = "Admin", emergency: emergency);
        var emergencyId = await host.CreateUserAsync("emergency@example.test", Password);
        emergency.Ids.Add(emergencyId);

        // The emergency account already holds the person's Microsoft login, so the sign-in would be linked to it.
        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            await users.AddLoginAsync(
                (await users.FindByIdAsync(emergencyId))!,
                new UserLoginInfo(AuthProviders.Microsoft, Ana.ToString("D"), AuthProviders.Microsoft));
        }

        await using (var scope = host.Scope())
        {
            var outcome = await Service(scope).CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"]));
            Assert.Equal(MicrosoftRefusal.EmergencyAccount, outcome.RefusalCode);
        }

        await using var check = host.Scope();
        var checkUsers = check.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var kept = await checkUsers.FindByIdAsync(emergencyId);
        Assert.Equal("emergency@example.test", kept!.Email);
        Assert.Empty(await checkUsers.GetRolesAsync(kept));
        Assert.Equal(emergencyId, (await checkUsers.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D")))!.Id);
    }

    [Fact]
    public async Task The_sync_leaves_an_emergency_account_alone_even_when_its_plan_would_switch_it_off()
    {
        var graph = new FakeGraph();
        var emergency = new FakeEmergency();
        await using var host = await HostAsync(graph: graph, sync: true, emergency: emergency);
        await MicrosoftAccountAsync(host, "ana@example.test", Ana);

        // The emergency account has the Microsoft login of a person the directory no longer has, so the plan would switch it off.
        var emergencyId = await host.CreateUserAsync("emergency@example.test", Password);
        emergency.Ids.Add(emergencyId);
        await using (var seed = host.Scope())
        {
            var users = seed.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            await users.AddLoginAsync(
                (await users.FindByIdAsync(emergencyId))!,
                new UserLoginInfo(AuthProviders.Microsoft, Bo.ToString("D"), AuthProviders.Microsoft));
        }

        graph.Snapshot = new MicrosoftDirectorySnapshot(
            [new MicrosoftAppRole(AdminRole, "Api.Admin")],
            [new MicrosoftAssignment(Ana, "User", AdminRole)],
            new Dictionary<Guid, MicrosoftUser> { [Ana] = new MicrosoftUser(Ana, "Ana", "Horvat", "ana@example.test", true) },
            null);

        await using var run = host.Scope();
        var result = await run.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        // The staff account stays on, so the run is not looks-wrong; the emergency account is neither switched off nor changed.
        Assert.Equal(MicrosoftSyncOutcome.Applied, result.Outcome);
        var users2 = run.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var kept = await users2.FindByIdAsync(emergencyId);
        Assert.False(await users2.IsLockedOutAsync(kept!));
        Assert.Equal("emergency@example.test", kept!.Email);
    }

    // A request whose services answer the handshake, as the library's complete route sees it.
    private static DefaultHttpContext Context(IAuthenticationService handshake)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handshake);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    // The one-time code is the query value of the sign-in page's address.
    private static string CodeFrom(string location) => Uri.UnescapeDataString(location["/admin/login.html?code=".Length..]);

    private const string MicrosoftCodeInvalid = "SIGNIN_CODE_INVALID";

    /// <summary>A directory whose read fails the way a refused Graph call does. Nothing is returned.</summary>
    private sealed class ThrowingGraph : IMicrosoftGraphClient
    {
        public Task<MicrosoftDirectorySnapshot> ReadDirectoryAsync(CancellationToken ct = default) =>
            throw new MicrosoftGraphException(DirectorySyncFailure.Graph, 403, "Microsoft answered 403.");
    }

    private static async Task<Harness<StringUser, string, StringRefresh>> HostAsync(
        FakeGraph? graph = null,
        bool sync = false,
        Action<MicrosoftSignInOptions>? configure = null,
        IMicrosoftEmergencyAccounts<string>? emergency = null)
    {
        var settings = new List<KeyValuePair<string, string?>>
        {
            new("MicrosoftSignIn:TenantId", Tenant),
            new("MicrosoftSignIn:ClientId", Client),
        };
        if (sync)
        {
            settings.Add(new("MicrosoftSignIn:ClientSecret", "test-client-secret-not-real"));
        }

        var configuration = TestConfig.Build(settings.ToArray());
        return await Harness<StringUser, string, StringRefresh>.CreateAsync(
            configuration,
            extra: services =>
            {
                services.AddBallastMicrosoftSignIn<StringUser, string, IdentityRole<string>, StringRefresh, StringDb>(
                    configuration,
                    "MicrosoftSignIn",
                    o =>
                    {
                        o.ConsolePrefix = "Api";
                        configure?.Invoke(o);
                    });
                services.AddSingleton<IMicrosoftGraphClient>(graph ?? new FakeGraph());
                services.AddSingleton<IJobLeadership>(new AlwaysHolderLeadership());
                if (emergency is not null)
                {
                    services.AddSingleton(emergency);
                }
            });
    }

    private static async Task<string> MicrosoftAccountAsync(Harness<StringUser, string, StringRefresh> host, string address, Guid objectId)
    {
        var userId = await host.CreateUserAsync(address, null);
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var user = await users.FindByIdAsync(userId);
        await users.AddLoginAsync(user!, new UserLoginInfo(AuthProviders.Microsoft, objectId.ToString("D"), AuthProviders.Microsoft));
        return userId;
    }

    private static async Task<string> IssueAsync(Harness<StringUser, string, StringRefresh> host, string userId)
    {
        await using var scope = host.Scope();
        return await Service(scope).IssueCodeAsync(userId);
    }

    // Each exchange runs in its own scope, as two requests would, so the race is between two contexts.
    private static async Task<string> ExchangeOutcomeAsync(Harness<StringUser, string, StringRefresh> host, string code)
    {
        await using var scope = host.Scope();
        try
        {
            await Service(scope).ExchangeAsync(code);
            return "ok";
        }
        catch (AppException ex)
        {
            return ex.Code;
        }
    }

    private static MsSignIn Service(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MsSignIn>();

    private static ClaimsPrincipal Principal(string address, Guid objectId, string[] roles)
    {
        var claims = new List<Claim>
        {
            new("tid", Tenant),
            new("oid", objectId.ToString("D")),
            new("preferred_username", address),
            new("given_name", "Ana"),
            new("family_name", "Horvat"),
        };
        claims.AddRange(roles.Select(role => new Claim("roles", role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
