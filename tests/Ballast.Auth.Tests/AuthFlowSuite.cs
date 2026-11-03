// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;

using Ballast.Errors;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Ballast.Auth.Tests;

/// <summary>
/// The sign-in flows, written once and run for each key type: a string key (GetThereAPI) and an int key (Chistify). Each test makes its
/// own database. The concrete classes choose the key and the refresh token entity.
/// </summary>
/// <typeparam name="TUser">The user.</typeparam>
/// <typeparam name="TKey">The key.</typeparam>
/// <typeparam name="TRefresh">The refresh token entity.</typeparam>
public abstract class AuthFlowSuite<TUser, TKey, TRefresh>
    where TUser : IdentityUser<TKey>, IAuthProfile, new()
    where TKey : IEquatable<TKey>
    where TRefresh : AuthRefreshTokenBase<TKey, TUser>, new()
{
    /// <summary>A password that meets the default rules: 12 characters, a digit, an upper-case letter and a symbol.</summary>
    protected const string Password = "Correct-horse-42!";

    private const string Email = "ana@example.test";

    /// <summary>A role made for the key type: the string key gets a GUID id, the int key takes the database's identity.</summary>
    /// <param name="name">The role's name.</param>
    protected abstract IdentityRole<TKey> NewRole(string name);

    /// <summary>The id, as text, of a user that does not exist, in the key type's form.</summary>
    protected abstract string MissingUserId { get; }

    private static async Task<Harness<TUser, TKey, TRefresh>> StartAsync(Action<AuthOptions>? configure = null, Action<IServiceCollection>? extra = null) =>
        await Harness<TUser, TKey, TRefresh>.CreateAsync(configure: configure, extra: extra);

    private static async Task<IssuedSession> SignInAsync(Harness<TUser, TKey, TRefresh> harness, string email = Email, bool rememberMe = false)
    {
        await using var scope = harness.Scope();
        var user = await scope.ServiceProvider
            .GetRequiredService<PasswordSignInService<TUser, TKey, AuthTestDb<TUser, TKey, TRefresh>>>()
            .CheckPasswordAsync(email, Password);
        return await scope.ServiceProvider
            .GetRequiredService<AuthSessionService<TUser, TKey, TRefresh, AuthTestDb<TUser, TKey, TRefresh>>>()
            .IssueAsync(user, rememberMe, "Login");
    }

    private static async Task<AppException> RefuseAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<AppException>(action);
        return error;
    }

    private static async Task<IssuedSession> RefreshAsync(Harness<TUser, TKey, TRefresh> harness, string raw)
    {
        await using var scope = harness.Scope();
        return await scope.ServiceProvider
            .GetRequiredService<AuthSessionService<TUser, TKey, TRefresh, AuthTestDb<TUser, TKey, TRefresh>>>()
            .RefreshAsync(raw);
    }

    private static async Task SetLockedAsync(Harness<TUser, TKey, TRefresh> harness, TKey userId, bool locked)
    {
        await using var scope = harness.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        var user = await users.FindByIdAsync(TestConfig.KeyText(userId)) ?? throw new InvalidOperationException("No test user.");
        await users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.UtcNow.AddHours(1) : null);
    }

    private static async Task<int> CountAuditAsync(Harness<TUser, TKey, TRefresh> harness, string action)
    {
        await using var scope = harness.Scope();
        return await scope.ServiceProvider.GetRequiredService<AuthTestDb<TUser, TKey, TRefresh>>()
            .AuditRows.CountAsync(a => a.Action == action);
    }

    [Fact]
    public async Task A_correct_password_issues_a_session_with_the_stored_claims()
    {
        await using var harness = await StartAsync();
        var userId = await harness.CreateUserAsync(Email, Password, "Ana Horvat");

        var session = await SignInAsync(harness);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(session.AccessToken);
        Assert.Equal(TestConfig.KeyText(userId), token.GetClaim("sub").Value);
        Assert.Equal(Email, token.GetClaim("email").Value);
        Assert.Equal("Ana Horvat", token.GetClaim("given_name").Value);
        Assert.False(string.IsNullOrEmpty(token.GetClaim("security_stamp").Value));
        Assert.Equal(TestConfig.Issuer, token.Issuer);
        Assert.Contains(TestConfig.Audience, token.Audiences);
    }

    [Fact]
    public async Task The_access_token_lasts_fifteen_minutes_by_default()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);

        var session = await SignInAsync(harness);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(session.AccessToken);
        Assert.Equal(harness.Time.Now.UtcDateTime.AddMinutes(15), token.ValidTo);
    }

    [Fact]
    public async Task The_refresh_token_is_64_random_bytes_in_base64_and_only_its_hash_is_stored()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);

        var session = await SignInAsync(harness);

        var raw = Convert.FromBase64String(session.RefreshToken);
        Assert.Equal(64, raw.Length);
        var expectedHash = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session.RefreshToken)));
        await using var scope = harness.Scope();
        var stored = await scope.ServiceProvider.GetRequiredService<AuthTestDb<TUser, TKey, TRefresh>>().RefreshTokens.SingleAsync();
        Assert.Equal(expectedHash, stored.Token);
        Assert.NotEqual(session.RefreshToken, stored.Token);
    }

    [Fact]
    public async Task An_unknown_address_and_a_wrong_password_get_the_same_answer()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);

        await using var scope = harness.Scope();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordSignInService<TUser, TKey, AuthTestDb<TUser, TKey, TRefresh>>>();
        var unknown = await RefuseAsync(() => passwords.CheckPasswordAsync("nobody@example.test", Password));
        var wrong = await RefuseAsync(() => passwords.CheckPasswordAsync(Email, "wrong-Password-1!"));

        Assert.Equal("INVALID_CREDENTIALS", unknown.Code);
        Assert.Equal(401, unknown.StatusCode);
        Assert.Equal(unknown.Code, wrong.Code);
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(unknown.Message, wrong.Message);
    }

    [Fact]
    public async Task Every_refusal_spends_exactly_one_hash_so_none_is_faster()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);
        var passwordless = "nopass@example.test";
        await harness.CreateUserAsync(passwordless, null);
        var locked = "locked@example.test";
        await harness.CreateUserAsync(locked, Password);
        var lockedId = await HarnessUserIdAsync(harness, locked);
        await SetLockedAsync(harness, lockedId, locked: true);
        var microsoft = "staff@example.test";
        var microsoftId = await harness.CreateUserAsync(microsoft, null);
        await using (var scope = harness.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
            var user = await users.FindByIdAsync(TestConfig.KeyText(microsoftId)) ?? throw new InvalidOperationException("No test user.");
            await users.AddLoginAsync(user, new UserLoginInfo(AuthProviders.Microsoft, "object-id", "Microsoft"));
        }

        var attempts = new (string Name, string Email, string Password)[]
        {
            ("unknown address", "nobody@example.test", Password),
            ("wrong password", Email, "wrong-Password-1!"),
            ("locked account", locked, Password),
            ("password-less account", passwordless, Password),
            ("Microsoft account", microsoft, Password),
        };

        foreach (var attempt in attempts)
        {
            harness.Hashes.Calls = 0;
            await using var scope = harness.Scope();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordSignInService<TUser, TKey, AuthTestDb<TUser, TKey, TRefresh>>>();
            var error = await RefuseAsync(() => passwords.CheckPasswordAsync(attempt.Email, attempt.Password));

            Assert.Equal("INVALID_CREDENTIALS", error.Code);
            Assert.True(harness.Hashes.Calls == 1, $"{attempt.Name} spent {harness.Hashes.Calls} hashes; every refusal must spend one.");
        }
    }

    [Fact]
    public async Task Five_failed_passwords_lock_the_account_and_refuse_even_the_right_password()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var scope = harness.Scope();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordSignInService<TUser, TKey, AuthTestDb<TUser, TKey, TRefresh>>>();
            _ = await RefuseAsync(() => passwords.CheckPasswordAsync(Email, "wrong-Password-1!"));
        }

        var error = await RefuseAsync(() => SignInAsync(harness));
        Assert.Equal("INVALID_CREDENTIALS", error.Code);
        Assert.True(await CountAuditAsync(harness, "LoginFailed") >= 5);
    }

    [Fact]
    public async Task A_password_sign_in_turned_off_is_refused_before_any_account_is_read()
    {
        await using var harness = await StartAsync(configure: o => o.PasswordSignIn = AuthPasswordSignInMode.Off);
        await harness.CreateUserAsync(Email, Password);
        harness.Hashes.Calls = 0;

        var error = await RefuseAsync(() => SignInAsync(harness));

        Assert.Equal("PASSWORD_SIGNIN_OFF", error.Code);
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(0, harness.Hashes.Calls);
    }

    [Fact]
    public async Task Owner_only_admits_the_privileged_role_and_refuses_the_rest_as_a_wrong_password()
    {
        await using var harness = await StartAsync(configure: o =>
        {
            o.PasswordSignIn = AuthPasswordSignInMode.OwnerOnly;
            o.PrivilegedRoleName = "Owner";
        });
        var owner = "owner@example.test";
        var staff = "staff@example.test";
        await harness.CreateUserAsync(owner, Password);
        await harness.CreateUserAsync(staff, Password);
        await using (var scope = harness.Scope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<TKey>>>();
            if (!await roles.RoleExistsAsync("Owner"))
            {
                await roles.CreateAsync(NewRole("Owner"));
            }

            var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
            var ownerUser = await users.FindByEmailAsync(owner) ?? throw new InvalidOperationException("No owner.");
            await users.AddToRoleAsync(ownerUser, "Owner");
        }

        var admitted = await SignInAsync(harness, owner);
        var refused = await RefuseAsync(() => SignInAsync(harness, staff));

        Assert.False(string.IsNullOrEmpty(admitted.AccessToken));
        Assert.Equal("INVALID_CREDENTIALS", refused.Code);
    }

    [Fact]
    public async Task A_refresh_rotates_and_the_old_token_is_refused_on_reuse_which_revokes_the_whole_family()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);
        var first = await SignInAsync(harness);

        var second = await RefreshAsync(harness, first.RefreshToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        var reuse = await RefuseAsync(() => RefreshAsync(harness, first.RefreshToken));
        Assert.Equal("REFRESH_TOKEN_EXPIRED", reuse.Code);
        Assert.Equal(401, reuse.StatusCode);

        // The reuse revoked the successor too: the family is gone.
        var afterReuse = await RefuseAsync(() => RefreshAsync(harness, second.RefreshToken));
        Assert.Equal("REFRESH_TOKEN_EXPIRED", afterReuse.Code);
        Assert.True(await CountAuditAsync(harness, "RefreshTokenReuseDetected") >= 1);
    }

    [Fact]
    public async Task Concurrent_refreshes_of_one_token_let_exactly_one_through()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);
        var session = await SignInAsync(harness);

        var attempts = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await RefreshAsync(harness, session.RefreshToken);
                    return (Ok: true, Code: string.Empty);
                }
                catch (AppException error)
                {
                    return (Ok: false, error.Code);
                }
            }))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.All(results.Where(r => !r.Ok), r => Assert.Equal("REFRESH_TOKEN_EXPIRED", r.Code));
    }

    [Fact]
    public async Task A_locked_account_cannot_refresh_even_with_a_good_token()
    {
        await using var harness = await StartAsync();
        var userId = await harness.CreateUserAsync(Email, Password);
        var session = await SignInAsync(harness);

        await SetLockedAsync(harness, userId, locked: true);
        var error = await RefuseAsync(() => RefreshAsync(harness, session.RefreshToken));

        Assert.Equal("REFRESH_TOKEN_EXPIRED", error.Code);
        Assert.True(await CountAuditAsync(harness, "RefreshRejectedForLockedAccount") >= 1);
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_an_unknown_token_is_not_an_error()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);
        var session = await SignInAsync(harness);

        await using (var scope = harness.Scope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<AuthSessionService<TUser, TKey, TRefresh, AuthTestDb<TUser, TKey, TRefresh>>>();
            await sessions.LogoutAsync(session.RefreshToken);
            await sessions.LogoutAsync("never-issued-token");
            await sessions.LogoutAsync(null);
        }

        var error = await RefuseAsync(() => RefreshAsync(harness, session.RefreshToken));
        Assert.Equal("REFRESH_TOKEN_EXPIRED", error.Code);
    }

    [Fact]
    public async Task A_blank_refresh_token_is_invalid_refresh_token()
    {
        await using var harness = await StartAsync();
        var error = await RefuseAsync(() => RefreshAsync(harness, "  "));

        Assert.Equal("INVALID_REFRESH_TOKEN", error.Code);
        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task Remember_me_is_read_back_from_the_lifetime_and_kept_on_refresh()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);

        var standard = await SignInAsync(harness, rememberMe: false);
        var remembered = await SignInAsync(harness, rememberMe: true);

        Assert.Equal(harness.Time.Now.UtcDateTime.AddDays(1), standard.RefreshTokenExpiresAtUtc);
        Assert.Equal(harness.Time.Now.UtcDateTime.AddDays(30), remembered.RefreshTokenExpiresAtUtc);

        var rotated = await RefreshAsync(harness, remembered.RefreshToken);
        Assert.Equal(harness.Time.Now.UtcDateTime.AddDays(30), rotated.RefreshTokenExpiresAtUtc);
    }

    [Fact]
    public async Task An_expired_refresh_token_is_refused()
    {
        await using var harness = await StartAsync();
        await harness.CreateUserAsync(Email, Password);
        var session = await SignInAsync(harness);

        harness.Time.Now = harness.Time.Now.AddDays(2);
        var error = await RefuseAsync(() => RefreshAsync(harness, session.RefreshToken));

        Assert.Equal("REFRESH_TOKEN_EXPIRED", error.Code);
    }

    [Fact]
    public async Task Changing_the_password_ends_the_other_sessions_and_changes_the_stamp()
    {
        await using var harness = await StartAsync();
        var userId = await harness.CreateUserAsync(Email, Password);
        var session = await SignInAsync(harness);
        await using (var scope = harness.Scope())
        {
            var stampBefore = (await scope.ServiceProvider.GetRequiredService<UserManager<TUser>>().FindByIdAsync(TestConfig.KeyText(userId)))!.SecurityStamp;
            harness.Hashes.Calls = 0;

            await scope.ServiceProvider.GetRequiredService<AuthAccountService<TUser, TKey, TRefresh, AuthTestDb<TUser, TKey, TRefresh>>>()
                .ChangePasswordAsync(TestConfig.KeyText(userId), Password, "New-horse-43!");

            var stampAfter = (await scope.ServiceProvider.GetRequiredService<IAuthAccountStore>().FindAsync(TestConfig.KeyText(userId)))!.SecurityStamp;
            Assert.NotEqual(stampBefore, stampAfter);
        }

        var error = await RefuseAsync(() => RefreshAsync(harness, session.RefreshToken));
        Assert.Equal("REFRESH_TOKEN_EXPIRED", error.Code);
        Assert.Equal(1, await CountAuditAsync(harness, "PasswordChanged"));
    }

    [Fact]
    public async Task A_wrong_current_password_on_change_gives_identitys_words_under_the_unclassified_code()
    {
        await using var harness = await StartAsync();
        var userId = await harness.CreateUserAsync(Email, Password);

        await using var scope = harness.Scope();
        var error = await RefuseAsync(() => scope.ServiceProvider.GetRequiredService<AuthAccountService<TUser, TKey, TRefresh, AuthTestDb<TUser, TKey, TRefresh>>>()
            .ChangePasswordAsync(TestConfig.KeyText(userId), "not-the-password-1!", "New-horse-43!"));

        Assert.Equal("UNCLASSIFIED", error.Code);
        Assert.Equal(400, error.StatusCode);
        Assert.Contains("Incorrect password", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_the_password_of_a_microsoft_account_is_refused()
    {
        await using var harness = await StartAsync();
        var userId = await harness.CreateUserAsync("staff@example.test", null);
        await using (var scope = harness.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
            var user = await users.FindByIdAsync(TestConfig.KeyText(userId)) ?? throw new InvalidOperationException("No test user.");
            await users.AddLoginAsync(user, new UserLoginInfo(AuthProviders.Microsoft, "object-id", "Microsoft"));
        }

        await using var check = harness.Scope();
        var error = await RefuseAsync(() => check.ServiceProvider.GetRequiredService<AuthAccountService<TUser, TKey, TRefresh, AuthTestDb<TUser, TKey, TRefresh>>>()
            .ChangePasswordAsync(TestConfig.KeyText(userId), Password, "New-horse-43!"));

        Assert.Equal("MICROSOFT_ACCOUNT", error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Setting_the_first_password_works_once_and_then_refuses()
    {
        await using var harness = await StartAsync();
        var userId = await harness.CreateUserAsync(Email, null);

        await using (var scope = harness.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthAccountService<TUser, TKey, TRefresh, AuthTestDb<TUser, TKey, TRefresh>>>()
                .SetPasswordAsync(TestConfig.KeyText(userId), Password);
        }

        var session = await SignInAsync(harness);
        Assert.False(string.IsNullOrEmpty(session.AccessToken));

        await using var again = harness.Scope();
        var error = await RefuseAsync(() => again.ServiceProvider.GetRequiredService<AuthAccountService<TUser, TKey, TRefresh, AuthTestDb<TUser, TKey, TRefresh>>>()
            .SetPasswordAsync(TestConfig.KeyText(userId), "Another-horse-44!"));
        Assert.Equal("PASSWORD_ALREADY_SET", error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task An_app_claim_source_adds_its_claims_to_the_token()
    {
        await using var harness = await StartAsync(extra: services => services.AddScoped<IAccessTokenClaims<TUser>, FixedClaims<TUser>>());
        await harness.CreateUserAsync(Email, Password);

        var session = await SignInAsync(harness);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(session.AccessToken);
        Assert.Equal(FixedClaims<TUser>.ClaimValue, token.GetClaim(FixedClaims<TUser>.ClaimType).Value);
    }

    [Fact]
    public async Task The_identity_account_store_reports_the_stamp_and_whether_the_account_may_sign_in()
    {
        await using var harness = await StartAsync();
        var userId = await harness.CreateUserAsync(Email, Password);
        await using var scope = harness.Scope();
        var store = scope.ServiceProvider.GetRequiredService<IAuthAccountStore>();

        var open = await store.FindAsync(TestConfig.KeyText(userId));
        Assert.NotNull(open);
        Assert.True(open.CanSignIn);
        Assert.Null(await store.FindAsync(MissingUserId));

        await SetLockedAsync(harness, userId, locked: true);
        await using var locked = harness.Scope();
        var lockedState = await locked.ServiceProvider.GetRequiredService<IAuthAccountStore>().FindAsync(TestConfig.KeyText(userId));
        Assert.NotNull(lockedState);
        Assert.False(lockedState.CanSignIn);
    }

    private static async Task<TKey> HarnessUserIdAsync(Harness<TUser, TKey, TRefresh> harness, string email)
    {
        await using var scope = harness.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("No test user.");
        return user.Id;
    }
}
