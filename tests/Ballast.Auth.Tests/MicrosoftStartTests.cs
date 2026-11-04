// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth.Tests;

/// <summary>
/// The start step's decision, through its public method: a refusal before Entra is asked, or the challenge with its callback and hint. No
/// database and no network.
/// </summary>
public class MicrosoftStartTests
{
    private const string CompletePath = "/auth/microsoft/complete";

    private static MicrosoftSignInOptions Options(Action<MicrosoftSignInOptions>? configure = null)
    {
        var options = new MicrosoftSignInOptions
        {
            TenantId = "11111111-2222-3333-4444-555555555555",
            ClientId = "66666666-7777-8888-9999-000000000000",
        };
        configure?.Invoke(options);
        return options;
    }

    [Fact]
    public void Without_a_tenant_and_a_client_the_start_redirects_with_not_configured_and_does_not_challenge()
    {
        var decision = MicrosoftSignInStart.Decide(new MicrosoftSignInOptions(), "ana@example.test", CompletePath);

        Assert.Equal("/admin/login.html?error=MS_NOT_CONFIGURED", decision.RedirectUrl);
        Assert.Null(decision.Properties);
    }

    [Fact]
    public void An_address_at_another_company_domain_is_refused_before_entra_is_asked()
    {
        var decision = MicrosoftSignInStart.Decide(Options(o => o.AllowedDomains = ["example.test"]), "ana@other.test", CompletePath);

        Assert.Equal("/admin/login.html?error=MS_NOT_COMPANY_ADDRESS", decision.RedirectUrl);
        Assert.Null(decision.Properties);
    }

    [Fact]
    public void A_company_address_challenges_entra_with_the_complete_route_and_the_lower_case_hint()
    {
        var decision = MicrosoftSignInStart.Decide(Options(o => o.AllowedDomains = ["example.test"]), "  ANA@Example.TEST ", CompletePath);

        Assert.Null(decision.RedirectUrl);
        Assert.Equal("Microsoft", decision.Scheme);
        Assert.NotNull(decision.Properties);
        Assert.Equal(CompletePath, decision.Properties!.RedirectUri);
        Assert.Equal("ana@example.test", decision.Properties.Items["login_hint"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    public void A_hint_that_is_not_an_address_is_left_out_and_the_challenge_still_goes_ahead(string? hint)
    {
        var decision = MicrosoftSignInStart.Decide(Options(o => o.AllowedDomains = ["example.test"]), hint, CompletePath);

        Assert.Null(decision.RedirectUrl);
        Assert.NotNull(decision.Properties);
        Assert.False(decision.Properties!.Items.ContainsKey("login_hint"));
    }

    [Fact]
    public void With_no_company_domain_any_address_challenges_with_its_hint()
    {
        var decision = MicrosoftSignInStart.Decide(Options(), "anyone@anywhere.test", CompletePath);

        Assert.Null(decision.RedirectUrl);
        Assert.Equal("anyone@anywhere.test", decision.Properties!.Items["login_hint"]);
    }
}
