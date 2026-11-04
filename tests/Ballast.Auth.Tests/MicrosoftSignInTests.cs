// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

using Ballast.Errors;
using Ballast.Jobs;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballast.Auth.Tests;

using IntSignIn = MicrosoftSignInService<ManualKeyUser, int, IdentityRole<int>, ManualKeyRefresh, AuthTestDb<ManualKeyUser, int, ManualKeyRefresh>>;
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

/// <summary>A logger that keeps each entry's level and message, so a test can read what was logged.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>The entries, in order.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>What the staff profile hook did, in order, and whether the account was already stored when the create hook ran.</summary>
internal sealed class StaffProfileLog
{
    /// <summary>The calls, as the hook's name and the subject.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Whether the account was already in the database when the create hook ran, or null when it has not run.</summary>
    public bool? StoredWhenCreating { get; set; }
}

/// <summary>The app's profile of a staff member for the string-keyed tests: it records each call, and refuses the one address it is told to.</summary>
internal sealed class FakeStaffProfile(StringDb db, StaffProfileLog log, string? failFor) : IMicrosoftStaffProfile<StringUser, string>
{
    /// <inheritdoc />
    public async Task CreatingAsync(StringUser user, MicrosoftPerson person, CancellationToken ct)
    {
        log.Calls.Add("creating:" + person.Address);
        log.StoredWhenCreating = await db.Users.AnyAsync(stored => stored.Id == user.Id, ct);
        if (RefusalFor(person.Address) is { } refusal)
        {
            throw refusal;
        }
    }

    /// <inheritdoc />
    public Task AppliedAsync(StringUser user, MicrosoftPerson person, CancellationToken ct)
    {
        log.Calls.Add("applied:" + person.Address);
        return RefusalFor(person.Address) is { } refusal ? Task.FromException(refusal) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SwitchedOffAsync(StringUser user, CancellationToken ct)
    {
        log.Calls.Add("switchedoff:" + user.Email);
        return Task.CompletedTask;
    }

    private InvalidOperationException? RefusalFor(string address) =>
        address == failFor ? new InvalidOperationException("The app's own profile table refused " + address + ".") : null;
}

/// <summary>The app's profile for the int-keyed test: it sets the account's key from its own table, before the insert.</summary>
internal sealed class ManualKeyProfile : IMicrosoftStaffProfile<ManualKeyUser, int>
{
    /// <summary>The key the app assigns.</summary>
    public const int AssignedId = 1001;

    /// <inheritdoc />
    public Task CreatingAsync(ManualKeyUser user, MicrosoftPerson person, CancellationToken ct)
    {
        user.Id = AssignedId;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AppliedAsync(ManualKeyUser user, MicrosoftPerson person, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SwitchedOffAsync(ManualKeyUser user, CancellationToken ct) => Task.CompletedTask;
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
    public async Task With_the_sync_off_the_sync_and_its_graph_client_still_resolve_and_the_sync_does_nothing()
    {
        // No client secret, so the sync is off. This host registers the real graph client, with no fake in its place, so the test
        // fails if the registration depends on the sync being on.
        var settings = new List<KeyValuePair<string, string?>>
        {
            new("MicrosoftSignIn:TenantId", Tenant),
            new("MicrosoftSignIn:ClientId", Client),
        };
        var configuration = TestConfig.Build(settings.ToArray());
        await using var host = await Harness<StringUser, string, StringRefresh>.CreateAsync(
            configuration,
            extra: services =>
            {
                services.AddBallastMicrosoftSignIn<StringUser, string, IdentityRole<string>, StringRefresh, StringDb>(
                    configuration,
                    "MicrosoftSignIn",
                    o =>
                    {
                        o.ConsolePrefix = "Api";
                    });
                services.AddSingleton<IJobLeadership>(new AlwaysHolderLeadership());
            });

        await using var scope = host.Scope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMicrosoftGraphClient>());
        var result = await scope.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.Off, result.Outcome);
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
            scope.ServiceProvider.GetRequiredService<IMicrosoftStaffProfile<StringUser, string>>(),
            host.Time,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MsSync>>());

        var result = await sync.RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.ReadFailed, result.Outcome);
        Assert.Equal(DirectorySyncFailure.Graph, result.Failure);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        Assert.False(await users.IsLockedOutAsync((await users.FindByIdAsync(userId))!));
    }

    [Fact]
    public async Task A_role_made_from_microsoft_that_no_assignment_names_any_more_is_listed_on_the_result_and_kept()
    {
        var graph = new FakeGraph();
        await using var host = await HostAsync(graph: graph, sync: true, configure: o => o.FullAccessRole = "Admin");
        await using (var scope = host.Scope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();
            var role = new IdentityRole<string>("Retired") { Id = Guid.NewGuid().ToString("D") };
            Assert.True((await roles.CreateAsync(role)).Succeeded);
            Assert.True((await roles.AddClaimAsync(role, new Claim(MicrosoftStaffMarks.SourceClaimType, MicrosoftStaffMarks.SourceMicrosoft))).Succeeded);
        }

        // Nobody is assigned in Microsoft any more, so the role made from it is no longer named.
        graph.Snapshot = Directory();

        await using var run = host.Scope();
        var result = await run.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        Assert.Equal("Retired", Assert.Single(result.RolesNotInMicrosoft));
        Assert.Contains(result.Problems, problem => problem.Code == MicrosoftSyncProblems.RoleNotInMicrosoft && problem.Subject == "Retired");
        await using var check = host.Scope();
        Assert.True(await check.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>().RoleExistsAsync("Retired"));
    }

    [Fact]
    public void The_public_marks_have_the_values_the_library_writes()
    {
        Assert.Equal("source", MicrosoftStaffMarks.SourceClaimType);
        Assert.Equal("microsoft", MicrosoftStaffMarks.SourceMicrosoft);
    }

    [Fact]
    public void The_switch_off_is_read_from_a_lockout_far_ahead_and_never_from_an_ordinary_lockout()
    {
        var now = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

        Assert.False(MicrosoftStaffMarks.IsSwitchedOff(null, now));
        Assert.False(MicrosoftStaffMarks.IsSwitchedOff(now.AddMinutes(15), now));
        Assert.False(MicrosoftStaffMarks.IsSwitchedOff(now.AddDays(1), now));
        Assert.True(MicrosoftStaffMarks.IsSwitchedOff(DateTimeOffset.MaxValue, now));
    }

    [Fact]
    public async Task A_sync_switch_off_is_shown_as_switched_off_and_a_failed_sign_in_lockout_is_not()
    {
        var graph = new FakeGraph();
        await using var host = await HostAsync(graph: graph, sync: true, configure: o => o.FullAccessRole = "Admin");
        var anaId = await MicrosoftAccountAsync(host, "ana@example.test", Ana);
        var boId = await MicrosoftAccountAsync(host, "bo@example.test", Bo);

        // Bo leaves the directory, and the sync switches Bo off. Ana stays, and is locked by nothing.
        graph.Snapshot = Directory((Ana, "ana@example.test", "Ana", "Horvat"));
        await using (var run = host.Scope())
        {
            await run.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();
        }

        await using var check = host.Scope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var now = DateTimeOffset.UtcNow;
        var bo = await users.FindByIdAsync(boId);
        var ana = await users.FindByIdAsync(anaId);
        Assert.True(MicrosoftStaffMarks.IsSwitchedOff((await users.GetLockoutEndDateAsync(bo!)), now));
        Assert.False(MicrosoftStaffMarks.IsSwitchedOff((await users.GetLockoutEndDateAsync(ana!)), now));
    }

    [Fact]
    public async Task A_handshake_whose_sign_in_throws_still_removes_its_cookie()
    {
        var log = new StaffProfileLog();
        await using var host = await HostAsync(profileLog: log, profileFailsFor: "ana@example.test");
        var handshake = new FakeHandshake { Principal = Principal("ana@example.test", Ana, ["Api.Admin"]) };

        await using var scope = host.Scope();
        await Assert.ThrowsAsync<MicrosoftStaffProfileException>(() => Service(scope).CompleteAsync(Context(handshake)));

        Assert.Equal(new List<string?> { "MicrosoftSignIn.Cookie" }, handshake.SignedOutSchemes);
    }

    [Fact]
    public async Task A_refused_sign_in_is_logged_at_warning_with_its_code_and_object_id_and_never_the_address()
    {
        var logger = new CapturingLogger<MsSignIn>();
        await using var host = await HostAsync(configure: o => o.AllowedDomains = ["example.test"], signInLogger: logger);

        await using var scope = host.Scope();
        var outcome = await Service(scope).CompleteAsync(Principal("ana@other.test", Ana, ["Api.Admin"]));

        Assert.False(outcome.IsAccepted);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(outcome.RefusalCode!, entry.Message, StringComparison.Ordinal);
        Assert.Contains(Ana.ToString("D"), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@other.test", entry.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task A_new_staff_member_is_offered_to_the_profile_hook_before_the_insert_and_is_not_stored_yet()
    {
        var log = new StaffProfileLog();
        await using var host = await HostAsync(configure: o => o.FullAccessRole = "Admin", profileLog: log);

        await using (var scope = host.Scope())
        {
            var outcome = await Service(scope).CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"]));
            Assert.True(outcome.IsAccepted);
        }

        Assert.Equal("creating:ana@example.test", Assert.Single(log.Calls));
        Assert.False(log.StoredWhenCreating);
    }

    [Fact]
    public async Task A_name_change_at_sign_in_is_offered_to_the_profile_hook_once_and_an_unchanged_sign_in_is_not()
    {
        var log = new StaffProfileLog();
        await using var host = await HostAsync(configure: o => o.FullAccessRole = "Admin", profileLog: log);
        await MicrosoftAccountAsync(host, "ana@example.test", Ana);

        await using (var scope = host.Scope())
        {
            var outcome = await Service(scope).CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"]));
            Assert.True(outcome.IsAccepted);
        }

        await using (var scope = host.Scope())
        {
            var outcome = await Service(scope).CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"]));
            Assert.True(outcome.IsAccepted);
        }

        Assert.Equal("applied:ana@example.test", Assert.Single(log.Calls));
        await using var check = host.Scope();
        var user = await check.ServiceProvider.GetRequiredService<UserManager<StringUser>>().FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D"));
        Assert.Equal("Ana Horvat", user!.FullName);
    }

    [Fact]
    public async Task A_staff_member_who_leaves_the_directory_is_switched_off_and_the_hook_is_told_with_that_account()
    {
        var graph = new FakeGraph();
        var log = new StaffProfileLog();
        await using var host = await HostAsync(graph: graph, sync: true, configure: o => o.FullAccessRole = "Admin", profileLog: log);
        await MicrosoftAccountAsync(host, "ana@example.test", Ana);
        await MicrosoftAccountAsync(host, "bo@example.test", Bo);

        // Ana is still assigned, and Bo has left the directory, so the run switches Bo off.
        graph.Snapshot = Directory((Ana, "ana@example.test", "Ana", "Horvat"));

        await using var scope = host.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.Applied, result.Outcome);
        Assert.Contains("switchedoff:bo@example.test", log.Calls);
        Assert.Contains("applied:ana@example.test", log.Calls);
        Assert.Equal(2, log.Calls.Count);
    }

    [Fact]
    public async Task A_create_whose_profile_hook_fails_rolls_back_the_account_and_its_role_and_the_error_reaches_the_caller()
    {
        var log = new StaffProfileLog();
        await using var host = await HostAsync(configure: o => o.FullAccessRole = "Admin", profileLog: log, profileFailsFor: "ana@example.test");

        await using (var scope = host.Scope())
        {
            await Assert.ThrowsAsync<MicrosoftStaffProfileException>(() => Service(scope).CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"])));
        }

        await using var check = host.Scope();
        var db = check.ServiceProvider.GetRequiredService<StringDb>();
        Assert.False(await db.Users.AnyAsync(user => user.Email == "ana@example.test"));
        Assert.False(await db.UserRoles.AnyAsync());
    }

    [Fact]
    public async Task A_staff_member_whose_profile_hook_fails_is_undone_alone_and_the_sync_reports_it_and_goes_on()
    {
        var graph = new FakeGraph();
        var log = new StaffProfileLog();
        await using var host = await HostAsync(graph: graph, sync: true, configure: o => o.FullAccessRole = "Admin", profileLog: log, profileFailsFor: "ana@example.test");
        await MicrosoftAccountAsync(host, "ana@example.test", Ana);
        await MicrosoftAccountAsync(host, "bo@example.test", Bo);
        graph.Snapshot = Directory((Ana, "ana@example.test", "Ana", "Horvat"), (Bo, "bo@example.test", "Bo", "Kovac"));

        await using var scope = host.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.Applied, result.Outcome);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(MicrosoftSyncProblems.ProfileHookFailed, problem.Code);
        Assert.Equal("ana@example.test", problem.Subject);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var ana = await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D"));
        Assert.Equal("Ana Test", ana!.FullName);
        Assert.Empty(await users.GetRolesAsync(ana));
        var bo = await users.FindByLoginAsync(AuthProviders.Microsoft, Bo.ToString("D"));
        Assert.Contains("Admin", await users.GetRolesAsync(bo!));
    }

    [Fact]
    public async Task With_the_switch_off_the_sync_creates_no_account_for_an_assigned_person_who_has_none()
    {
        var graph = new FakeGraph();
        var log = new StaffProfileLog();
        await using var host = await HostAsync(graph: graph, sync: true, configure: o => o.FullAccessRole = "Admin", profileLog: log);
        graph.Snapshot = Directory((Ana, "ana@example.test", "Ana", "Horvat"));

        await using var scope = host.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.Applied, result.Outcome);
        Assert.Equal(0, result.Counts!.Created);
        Assert.Empty(log.Calls);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        Assert.Null(await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D")));
        Assert.False(await scope.ServiceProvider.GetRequiredService<StringDb>().Users.AnyAsync(user => user.Email == "ana@example.test"));
    }

    [Fact]
    public async Task With_the_switch_on_the_sync_creates_an_assigned_person_through_the_hook_and_links_the_login()
    {
        var graph = new FakeGraph();
        var log = new StaffProfileLog();
        await using var host = await HostAsync(graph: graph, sync: true, configure: o => CreatingOn(o), profileLog: log);
        graph.Snapshot = Directory((Ana, "ana@example.test", "Ana", "Horvat"));

        await using (var scope = host.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();
            Assert.Equal(MicrosoftSyncOutcome.Applied, result.Outcome);
            Assert.Equal(1, result.Counts!.Created);
            Assert.Empty(result.Problems);
        }

        Assert.Equal("creating:ana@example.test", Assert.Single(log.Calls));
        Assert.False(log.StoredWhenCreating);

        await using var check = host.Scope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var user = await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D"));
        Assert.NotNull(user);
        Assert.Equal("ana@example.test", user.Email);
        Assert.Equal("Ana Horvat", user.FullName);
        Assert.Contains("Admin", await users.GetRolesAsync(user));
    }

    [Fact]
    public async Task With_the_switch_on_an_address_an_unlinked_account_holds_is_a_problem_row_and_no_login_is_linked_to_it()
    {
        var graph = new FakeGraph();
        var log = new StaffProfileLog();
        await using var host = await HostAsync(graph: graph, sync: true, configure: o => CreatingOn(o), profileLog: log);
        await host.CreateUserAsync("ana@example.test", null);
        graph.Snapshot = Directory((Ana, "ana@example.test", "Ana", "Horvat"));

        await using var scope = host.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.Applied, result.Outcome);
        Assert.Equal(0, result.Counts!.Created);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(MicrosoftSyncProblems.AddressTaken, problem.Code);
        Assert.Empty(log.Calls);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        Assert.Null(await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D")));
    }

    [Fact]
    public async Task With_the_switch_on_a_creation_whose_profile_hook_fails_is_undone_alone_and_the_sync_reports_it_and_goes_on()
    {
        var graph = new FakeGraph();
        var log = new StaffProfileLog();
        await using var host = await HostAsync(graph: graph, sync: true, configure: o => CreatingOn(o), profileLog: log, profileFailsFor: "ana@example.test");
        graph.Snapshot = Directory((Ana, "ana@example.test", "Ana", "Horvat"), (Bo, "bo@example.test", "Bo", "Kovac"));

        await using var scope = host.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<MsSync>().RunOnceAsync();

        Assert.Equal(MicrosoftSyncOutcome.Applied, result.Outcome);
        Assert.Equal(1, result.Counts!.Created);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(MicrosoftSyncProblems.ProfileHookFailed, problem.Code);
        Assert.Equal("ana@example.test", problem.Subject);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        Assert.Null(await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D")));
        Assert.False(await scope.ServiceProvider.GetRequiredService<StringDb>().Users.AnyAsync(user => user.Email == "ana@example.test"));
        var bo = await users.FindByLoginAsync(AuthProviders.Microsoft, Bo.ToString("D"));
        Assert.NotNull(bo);
        Assert.Contains("Admin", await users.GetRolesAsync(bo));
    }

    [Fact]
    public void The_plan_counts_the_creations_it_would_make_and_none_with_the_switch_off()
    {
        var directory = Directory((Ana, "ana@example.test", "Ana", "Horvat"), (Bo, "bo@example.test", "Bo", "Kovac"));
        var options = new MicrosoftSignInOptions { TenantId = Tenant, ClientId = Client, ConsolePrefix = "Api", FullAccessRole = "Admin" };

        var off = MicrosoftSyncPlan.Plan(directory, options, [], [], new Dictionary<string, string>(), new HashSet<string>());
        Assert.Equal(0, off.Counts.Created);
        Assert.Empty(off.Creations);

        options.SyncCreatesAccounts = true;
        var on = MicrosoftSyncPlan.Plan(directory, options, [], [], new Dictionary<string, string>(), new HashSet<string>());
        Assert.Equal(2, on.Counts.Created);
        Assert.Equal(2, on.Creations.Count);
        Assert.Contains(on.Creations, creation => creation.ObjectId == Ana.ToString("D") && creation.Roles.Contains("Admin"));
    }

    private static void CreatingOn(MicrosoftSignInOptions options)
    {
        options.FullAccessRole = "Admin";
        options.SyncCreatesAccounts = true;
    }

    [Fact]
    public async Task An_int_keyed_app_sets_the_account_key_in_its_hook_and_the_insert_keeps_that_key()
    {
        await using var host = await IntHostAsync();

        await using (var scope = host.Scope())
        {
            var outcome = await scope.ServiceProvider.GetRequiredService<IntSignIn>()
                .CompleteAsync(Principal("ana@example.test", Ana, ["Api.Admin"]));
            Assert.True(outcome.IsAccepted);
            Assert.Equal(ManualKeyProfile.AssignedId, outcome.UserId);
        }

        await using var check = host.Scope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<ManualKeyUser>>();
        var user = await users.FindByLoginAsync(AuthProviders.Microsoft, Ana.ToString("D"));
        Assert.NotNull(user);
        Assert.Equal(ManualKeyProfile.AssignedId, user.Id);
        Assert.Contains("Admin", await users.GetRolesAsync(user));
    }

    private static MicrosoftDirectorySnapshot Directory(params (Guid Id, string Address, string GivenName, string Surname)[] people) => new(
        [new MicrosoftAppRole(AdminRole, "Api.Admin")],
        [.. people.Select(person => new MicrosoftAssignment(person.Id, "User", AdminRole))],
        people.ToDictionary(person => person.Id, person => new MicrosoftUser(person.Id, person.GivenName, person.Surname, person.Address, true)),
        null);

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
        IMicrosoftEmergencyAccounts<string>? emergency = null,
        StaffProfileLog? profileLog = null,
        string? profileFailsFor = null,
        ILogger<MsSignIn>? signInLogger = null)
    {
        var log = profileLog ?? new StaffProfileLog();
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
                // The app's profile replaces the no-op default, because the registration after it is the one that resolves.
                services.AddScoped<IMicrosoftStaffProfile<StringUser, string>>(provider =>
                    new FakeStaffProfile(provider.GetRequiredService<StringDb>(), log, profileFailsFor));
                if (signInLogger is not null)
                {
                    services.AddSingleton(signInLogger);
                }
                services.AddSingleton<IMicrosoftGraphClient>(graph ?? new FakeGraph());
                services.AddSingleton<IJobLeadership>(new AlwaysHolderLeadership());
                if (emergency is not null)
                {
                    services.AddSingleton(emergency);
                }
            });
    }

    private static async Task<Harness<ManualKeyUser, int, ManualKeyRefresh>> IntHostAsync()
    {
        var settings = new List<KeyValuePair<string, string?>>
        {
            new("MicrosoftSignIn:TenantId", Tenant),
            new("MicrosoftSignIn:ClientId", Client),
        };

        var configuration = TestConfig.Build(settings.ToArray());
        return await Harness<ManualKeyUser, int, ManualKeyRefresh>.CreateAsync(
            configuration,
            extra: services =>
            {
                services.AddBallastMicrosoftSignIn<ManualKeyUser, int, IdentityRole<int>, ManualKeyRefresh, AuthTestDb<ManualKeyUser, int, ManualKeyRefresh>>(
                    configuration,
                    "MicrosoftSignIn",
                    o =>
                    {
                        o.ConsolePrefix = "Api";
                        o.FullAccessRole = "Admin";
                    });
                services.AddScoped<IMicrosoftStaffProfile<ManualKeyUser, int>, ManualKeyProfile>();
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
