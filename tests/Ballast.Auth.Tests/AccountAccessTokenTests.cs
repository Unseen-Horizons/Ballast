// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Security.Claims;

using Ballast.Errors;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Ballast.Auth.Tests;

/// <summary>A store for accounts outside Identity, such as portal contacts. The tests change a stamp or a sign-in right by hand.</summary>
internal sealed class PortalAccounts : IAuthAccountStore
{
    private readonly Dictionary<string, AuthAccountState> _accounts = new();

    /// <summary>Adds an account.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="stamp">The security stamp.</param>
    /// <param name="canSignIn">Whether the account may sign in.</param>
    public void Add(string subject, string stamp, bool canSignIn = true) => _accounts[subject] = new AuthAccountState(stamp, canSignIn);

    /// <summary>Gives the account a new stamp, as a password or permission change does.</summary>
    /// <param name="subject">The subject.</param>
    public void ChangeStamp(string subject) => _accounts[subject] = _accounts[subject] with { SecurityStamp = Guid.NewGuid().ToString("N") };

    /// <summary>Locks or unlocks the account.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="canSignIn">Whether it may sign in.</param>
    public void SetCanSignIn(string subject, bool canSignIn) => _accounts[subject] = _accounts[subject] with { CanSignIn = canSignIn };

    /// <inheritdoc />
    public Task<AuthAccountState?> FindAsync(string subject, CancellationToken ct = default) =>
        Task.FromResult(_accounts.TryGetValue(subject, out var state) ? state : null);
}

/// <summary>The access token for an account outside Identity: it is accepted, cut off by its stamp, and refused when the account stops.</summary>
public class AccountAccessTokenTests
{
    private const string Subject = "contact:42";

    [Fact]
    public async Task A_portal_token_passes_the_bearer_pipeline_and_a_changed_stamp_refuses_it()
    {
        var accounts = new PortalAccounts();
        accounts.Add(Subject, "stamp-1");
        await using var host = await PipelineHost.StartAsync(accounts);
        var token = await IssueAsync(host, TimeSpan.FromMinutes(30));

        using (var allowed = await host.CallAsync(token))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        accounts.ChangeStamp(Subject);

        using var refused = await host.CallAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_portal_account_is_refused_at_the_next_request()
    {
        var accounts = new PortalAccounts();
        accounts.Add(Subject, "stamp-1");
        await using var host = await PipelineHost.StartAsync(accounts);
        var token = await IssueAsync(host, TimeSpan.FromMinutes(30));

        accounts.SetCanSignIn(Subject, false);

        using var refused = await host.CallAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task The_token_carries_the_library_claims_and_the_app_claims()
    {
        var accounts = new PortalAccounts();
        accounts.Add(Subject, "stamp-1");
        await using var host = await PipelineHost.StartAsync(accounts);
        var token = await IssueAsync(host, TimeSpan.FromMinutes(30));

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        Assert.Equal(Subject, jwt.Subject);
        Assert.Equal("portal", jwt.Claims.Single(c => c.Type == "auth_type").Value);
        Assert.Equal("Client", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal("stamp-1", jwt.Claims.Single(c => c.Type == "security_stamp").Value);
        Assert.Equal(TestConfig.Issuer, jwt.Issuer);
    }

    [Fact]
    public async Task An_account_that_cannot_sign_in_gets_no_token()
    {
        var accounts = new PortalAccounts();
        accounts.Add(Subject, "stamp-1", canSignIn: false);
        await using var host = await PipelineHost.StartAsync(accounts);

        await using var scope = host.Scope();
        var tokens = scope.ServiceProvider.GetRequiredService<AccountAccessTokens>();
        var error = await Assert.ThrowsAsync<AppException>(() => tokens.CreateAsync(Subject, TimeSpan.FromMinutes(30)));

        Assert.Equal("INVALID_CREDENTIALS", error.Code);
    }

    [Fact]
    public async Task A_lifetime_of_zero_or_an_extra_claim_the_library_sets_is_refused()
    {
        var accounts = new PortalAccounts();
        accounts.Add(Subject, "stamp-1");
        await using var host = await PipelineHost.StartAsync(accounts);

        await using var scope = host.Scope();
        var tokens = scope.ServiceProvider.GetRequiredService<AccountAccessTokens>();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tokens.CreateAsync(Subject, TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentException>(() => tokens.CreateAsync(Subject, TimeSpan.FromMinutes(5), [new Claim("sub", "someone-else")]));
    }

    /// <summary>Issues a portal token the way the app does: its own claims, and the lifetime it chooses.</summary>
    private static async Task<string> IssueAsync(PipelineHost host, TimeSpan lifetime)
    {
        await using var scope = host.Scope();
        var tokens = scope.ServiceProvider.GetRequiredService<AccountAccessTokens>();
        return await tokens.CreateAsync(Subject, lifetime, [new Claim("auth_type", "portal"), new Claim("role", "Client")]);
    }
}
