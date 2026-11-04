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
}
