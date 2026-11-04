// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.Auth.Tests;

using StringDb = AuthTestDb<StringUser, string, StringRefresh>;

/// <summary>A Google check that answers from a table, so the tests need no network. A token that is not in the table is refused.</summary>
internal sealed class FakeGoogle : IGoogleIdTokenValidator
{
    private readonly Dictionary<string, GoogleIdentity> _known = new();

    /// <summary>How many tokens were checked, so a test can see that Google was not asked.</summary>
    public int Calls { get; private set; }

    /// <summary>Makes a token valid for an identity.</summary>
    /// <param name="token">The token the test presents.</param>
    /// <param name="identity">What Google would vouch for.</param>
    public void Add(string token, GoogleIdentity identity) => _known[token] = identity;

    /// <inheritdoc />
    public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult<GoogleIdentity?>(_known.TryGetValue(idToken, out var identity) ? identity : null);
    }
}

/// <summary>The app's rows for a new account: a marker the test can find, written through the test context so it is saved with the account.</summary>
internal sealed class MarkerAccountRows : IAuthNewAccount<StringUser>
{
    private readonly StringDb _db;

    public MarkerAccountRows(StringDb db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task AddRowsAsync(StringUser user, CancellationToken ct = default)
    {
        _db.AuditRows.Add(new AuditRow
        {
            UserId = user.Id,
            Action = "NewAccountRows",
            EntityType = "Wallet",
            EntityId = user.Id,
            CreatedAtUtc = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc),
        });
        return Task.CompletedTask;
    }
}

/// <summary>Google sign-in on SQL Server: first sign-in, later sign-ins, the refusals, linking and unlinking.</summary>
public class GoogleSignInTests
{
    private const string Password = "Correct-horse-42!";

    private static readonly GoogleIdentity Ana = new("google-subject-ana", "ana@example.test", true, "Ana Google");

    [Fact]
    public async Task A_first_sign_in_creates_a_passwordless_account_with_its_role_login_app_rows_and_a_session()
    {
        var google = new FakeGoogle();
        google.Add("token-ana", Ana);
        await using var host = await HostAsync(google, o => o.NewAccountRole = "User");
        await CreateRoleAsync(host, "User");

        await using (var scope = host.Scope())
        {
            var session = await Service(scope).SignInAsync("token-ana", rememberMe: false);
            Assert.False(string.IsNullOrEmpty(session.AccessToken));
            Assert.False(string.IsNullOrEmpty(session.RefreshToken));
        }

        await using var check = host.Scope();
        var db = check.ServiceProvider.GetRequiredService<StringDb>();
        var user = await db.Users.SingleAsync();
        Assert.Equal("ana@example.test", user.Email);
        Assert.Equal("Ana Google", user.FullName);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);

        var login = await db.UserLogins.SingleAsync();
        Assert.Equal(AuthProviders.Google, login.LoginProvider);
        Assert.Equal("google-subject-ana", login.ProviderKey);

        var role = await db.UserRoles.SingleAsync();
        Assert.Equal("User", (await db.Roles.SingleAsync(r => r.Id == role.RoleId)).Name);

        Assert.Single(await db.AuditRows.Where(a => a.Action == "NewAccountRows").ToListAsync());
        Assert.Single(await db.AuditRows.Where(a => a.Action == "GoogleAccountCreated").ToListAsync());
    }

    [Fact]
    public async Task A_later_sign_in_with_the_same_google_account_signs_into_the_same_user()
    {
        var google = new FakeGoogle();
        google.Add("token-ana", Ana);
        await using var host = await HostAsync(google);

        await using (var scope = host.Scope())
        {
            await Service(scope).SignInAsync("token-ana", rememberMe: false);
        }

        await using (var scope = host.Scope())
        {
            var session = await Service(scope).SignInAsync("token-ana", rememberMe: true);
            Assert.False(string.IsNullOrEmpty(session.RefreshToken));
        }

        await using var check = host.Scope();
        var db = check.ServiceProvider.GetRequiredService<StringDb>();
        Assert.Single(await db.Users.ToListAsync());
        Assert.Equal(2, (await db.AuditRows.Where(a => a.Action == "GoogleLogin").ToListAsync()).Count);
    }

    [Fact]
    public async Task An_unverified_google_address_creates_nothing()
    {
        var google = new FakeGoogle();
        google.Add("token-unverified", new GoogleIdentity("google-subject-unverified", "unverified@example.test", false, null));
        await using var host = await HostAsync(google);

        await using var scope = host.Scope();
        var error = await Assert.ThrowsAsync<AppException>(() => Service(scope).SignInAsync("token-unverified", rememberMe: false));

        Assert.Equal("GOOGLE_EMAIL_UNVERIFIED", error.Code);
        Assert.Equal(403, error.StatusCode);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<StringDb>().Users.ToListAsync());
    }

    [Fact]
    public async Task A_google_address_that_belongs_to_a_password_account_is_refused_and_never_linked()
    {
        var google = new FakeGoogle();
        google.Add("token-ana", Ana);
        await using var host = await HostAsync(google);
        await host.CreateUserAsync("ana@example.test", Password);

        await using var scope = host.Scope();
        var error = await Assert.ThrowsAsync<AppException>(() => Service(scope).SignInAsync("token-ana", rememberMe: false));

        Assert.Equal("ACCOUNT_EXISTS_USE_PASSWORD", error.Code);
        Assert.Equal(409, error.StatusCode);

        var db = scope.ServiceProvider.GetRequiredService<StringDb>();
        Assert.Empty(await db.UserLogins.ToListAsync());
        Assert.Single(await db.AuditRows.Where(a => a.Action == "GoogleSignInRefusedExistingAccount").ToListAsync());
    }

    [Fact]
    public async Task A_token_Google_does_not_vouch_for_is_refused_as_invalid()
    {
        await using var host = await HostAsync(new FakeGoogle());

        await using var scope = host.Scope();
        var error = await Assert.ThrowsAsync<AppException>(() => Service(scope).SignInAsync("garbage", rememberMe: false));

        Assert.Equal("GOOGLE_TOKEN_INVALID", error.Code);
        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task A_locked_google_account_gets_the_same_answer_as_a_bad_password()
    {
        var google = new FakeGoogle();
        google.Add("token-ana", Ana);
        await using var host = await HostAsync(google);

        await using (var scope = host.Scope())
        {
            await Service(scope).SignInAsync("token-ana", rememberMe: false);
        }

        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            var user = await users.FindByEmailAsync("ana@example.test");
            await users.SetLockoutEnabledAsync(user!, true);
            await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1));
        }

        await using var check = host.Scope();
        var error = await Assert.ThrowsAsync<AppException>(() => Service(check).SignInAsync("token-ana", rememberMe: false));

        Assert.Equal("INVALID_CREDENTIALS", error.Code);
        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task Linking_adds_the_google_login_once_and_refuses_a_second_link_or_an_account_held_by_another_user()
    {
        var google = new FakeGoogle();
        google.Add("token-ana", Ana);
        await using var host = await HostAsync(google);
        var anaId = await host.CreateUserAsync("ana@example.test", Password);
        var benId = await host.CreateUserAsync("ben@example.test", Password);

        await using (var scope = host.Scope())
        {
            await Service(scope).LinkAsync(anaId, "token-ana");
        }

        await using (var scope = host.Scope())
        {
            var again = await Assert.ThrowsAsync<AppException>(() => Service(scope).LinkAsync(anaId, "token-ana"));
            Assert.Equal("GOOGLE_ALREADY_LINKED", again.Code);
            Assert.Equal(409, again.StatusCode);

            var otherUser = await Assert.ThrowsAsync<AppException>(() => Service(scope).LinkAsync(benId, "token-ana"));
            Assert.Equal("GOOGLE_ALREADY_LINKED", otherUser.Code);
        }

        await using var check = host.Scope();
        var db = check.ServiceProvider.GetRequiredService<StringDb>();
        var login = await db.UserLogins.SingleAsync();
        Assert.Equal(anaId, login.UserId);
    }

    [Fact]
    public async Task A_microsoft_account_cannot_link_google_and_Google_is_not_asked()
    {
        var google = new FakeGoogle();
        google.Add("token-ana", Ana);
        await using var host = await HostAsync(google);
        var anaId = await host.CreateUserAsync("ana@example.test", null);

        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            var user = await users.FindByIdAsync(anaId);
            await users.AddLoginAsync(user!, new UserLoginInfo(AuthProviders.Microsoft, "microsoft-object-id", AuthProviders.Microsoft));
        }

        await using var scope2 = host.Scope();
        var error = await Assert.ThrowsAsync<AppException>(() => Service(scope2).LinkAsync(anaId, "token-ana"));

        Assert.Equal("MICROSOFT_ACCOUNT", error.Code);
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(0, google.Calls);
    }

    [Fact]
    public async Task Unlinking_is_refused_when_it_would_leave_no_way_in_and_otherwise_removes_the_login()
    {
        var google = new FakeGoogle();
        google.Add("token-ana", Ana);
        await using var host = await HostAsync(google);

        string anaId;
        await using (var scope = host.Scope())
        {
            var session = await Service(scope).SignInAsync("token-ana", rememberMe: false);
            Assert.NotNull(session);
            anaId = (await scope.ServiceProvider.GetRequiredService<StringDb>().Users.SingleAsync()).Id;
        }

        await using (var scope = host.Scope())
        {
            var last = await Assert.ThrowsAsync<AppException>(() => Service(scope).UnlinkAsync(anaId));
            Assert.Equal("LAST_SIGN_IN_METHOD", last.Code);
            Assert.Equal(409, last.StatusCode);

            var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            await users.AddPasswordAsync((await users.FindByIdAsync(anaId))!, Password);
        }

        await using (var scope = host.Scope())
        {
            await Service(scope).UnlinkAsync(anaId);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<StringDb>().UserLogins.ToListAsync());

            var again = await Assert.ThrowsAsync<AppException>(() => Service(scope).UnlinkAsync(anaId));
            Assert.Equal("GOOGLE_NOT_LINKED", again.Code);
            Assert.Equal(404, again.StatusCode);
        }
    }

    [Fact]
    public async Task Without_a_web_client_id_the_validator_answers_not_configured_and_the_server_still_starts()
    {
        var validator = new GoogleIdTokenValidator(new AuthOptions(), new AuthGoogleOptions(), new GoogleJsonWebSignatureVerifier(), NullLogger<GoogleIdTokenValidator>.Instance);

        var error = await Assert.ThrowsAsync<AppException>(() => validator.ValidateAsync("anything"));

        Assert.Equal("GOOGLE_SIGNIN_NOT_CONFIGURED", error.Code);
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task A_token_that_is_not_a_jwt_is_refused_as_invalid_not_raised_as_an_error()
    {
        var validator = new GoogleIdTokenValidator(
            new AuthOptions(),
            new AuthGoogleOptions { WebClientId = "test-web-client" },
            new GoogleJsonWebSignatureVerifier(),
            NullLogger<GoogleIdTokenValidator>.Instance);

        Assert.Null(await validator.ValidateAsync("not-a-token"));
    }

    private static async Task<Harness<StringUser, string, StringRefresh>> HostAsync(FakeGoogle google, Action<AuthGoogleOptions>? configure = null)
    {
        return await Harness<StringUser, string, StringRefresh>.CreateAsync(extra: services =>
        {
            services.AddScoped<IAuthNewAccount<StringUser>, MarkerAccountRows>();
            services.AddSingleton<IGoogleIdTokenValidator>(google);
            services.AddBallastGoogleSignIn<StringUser, string, StringRefresh, StringDb>(
                TestConfig.Build(new KeyValuePair<string, string?>("Google:WebClientId", "test-web-client")),
                configure: configure);
        });
    }

    private static async Task CreateRoleAsync(Harness<StringUser, string, StringRefresh> host, string name)
    {
        await using var scope = host.Scope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<string>>>();
        var result = await roles.CreateAsync(new IdentityRole(name));
        Assert.True(result.Succeeded);
    }

    private static GoogleSignInService<StringUser, string, StringRefresh, StringDb> Service(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<GoogleSignInService<StringUser, string, StringRefresh, StringDb>>();
}
