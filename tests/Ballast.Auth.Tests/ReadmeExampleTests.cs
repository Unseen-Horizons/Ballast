// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.Auth.Tests;

/// <summary>Runs the example in src/Ballast.Auth/README.md, as written there, so the README cannot drift from the code.</summary>
public class ReadmeExampleTests
{
    // A contact in a portal is not an Identity user. The store answers whether the subject may sign in and what its security stamp is.
    private sealed class PortalContacts : IAuthAccountStore
    {
        public Task<AuthAccountState?> FindAsync(string subject, CancellationToken ct = default) =>
            Task.FromResult<AuthAccountState?>(subject == "contact:42" ? new AuthAccountState("stamp-1", CanSignIn: true) : null);
    }

    [Fact]
    public async Task The_readme_example_issues_a_token_that_validates_with_the_subject_it_names()
    {
        var options = new AuthOptions
        {
            Key = "replace-with-a-secret-of-at-least-32-bytes",
            Issuer = "https://portal.example.test",
            Audience = "portal-clients",
        };
        var tokens = new AccountAccessTokens(options, new PortalContacts(), TimeProvider.System);
        var token = await tokens.CreateAsync("contact:42", TimeSpan.FromMinutes(10));

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
        };
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.True(result.IsValid);
        Assert.Equal("contact:42", result.ClaimsIdentity.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task The_readme_example_refuses_a_subject_the_store_does_not_know()
    {
        var options = new AuthOptions
        {
            Key = "replace-with-a-secret-of-at-least-32-bytes",
            Issuer = "https://portal.example.test",
            Audience = "portal-clients",
        };
        var tokens = new AccountAccessTokens(options, new PortalContacts(), TimeProvider.System);

        await Assert.ThrowsAsync<Ballast.Errors.AppException>(() => tokens.CreateAsync("contact:99", TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void The_readme_sync_status_example_reports_an_applied_run_and_a_soon_secret()
    {
        var status = new MicrosoftSyncStatus();
        var ended = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

        // The job records the run's result when it ends. The library's sync returns the result.
        status.Record(new MicrosoftSyncResult(
            MicrosoftSyncOutcome.Applied,
            Failure: null,
            HttpStatus: null,
            Counts: new MicrosoftSyncCounts(UsersAssigned: 12, AppRoles: 3, Updated: 1, SwitchedOff: 0, Created: 0),
            Problems: [new MicrosoftSyncProblem("ROLE_NOT_IN_MICROSOFT", "Retired")],
            RolesNotInMicrosoft: ["Retired"],
            ClientSecretEndsUtc: ended.AddDays(10)), ended);

        // The admin page reads a snapshot. The client secret ends in ten days, so it reads Soon.
        var snapshot = status.Snapshot(ended);

        Assert.Equal(MicrosoftSyncLastRun.Applied, snapshot.LastRun);
        Assert.Equal(ClientSecretState.Soon, snapshot.ClientSecretState);
        Assert.True(status.IsNotInMicrosoft("Retired"));
    }
}
