// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;

namespace Ballast.Http.Tests;

/// <summary>
/// The per-host credentials, ported from GetThere's <c>FeedBasicAuthTests</c> and <c>FeedBasicAuthParityTests</c>. The two copies that
/// the parity test compared are one class now, so the parity cases become the cases of the one reader, with the section names
/// passed as options (the apps' own names, <c>Feeds:BasicAuth</c> and <c>Feeds:BasicAuthHosts</c>, are used here as the example).
/// </summary>
public class HostCredentialsTests
{
    private static readonly HostCredentialsOptions Apps = new()
    {
        SectionName = "Feeds:BasicAuthHosts",
        MapSectionName = "Feeds:BasicAuth",
    };

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Reads_the_host_keyed_map()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuth:b2b.promet-info.hr"] = "user:pass"
        }), Apps);

        Assert.Equal("user:pass", result["b2b.promet-info.hr"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_host_keyed_entry_with_no_credential_fails_loudly(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuth:b2b.promet-info.hr"] = value
        }), Apps));

        Assert.Contains("b2b.promet-info.hr", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Feeds:BasicAuth", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_indexed_list()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuthHosts:0:Host"] = "b2b.promet-info.hr",
            ["Feeds:BasicAuthHosts:0:Credential"] = "user:pass"
        }), Apps);

        Assert.Equal("user:pass", result["b2b.promet-info.hr"]);
    }

    /// <summary>Host matching is case-insensitive because the lookup key is the URL's host, which keeps the casing it was written in.</summary>
    [Fact]
    public void Matches_the_host_case_insensitively()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuthHosts:0:Host"] = "B2B.Promet-Info.HR",
            ["Feeds:BasicAuthHosts:0:Credential"] = "user:pass"
        }), Apps);

        Assert.True(result.TryGetValue("b2b.promet-info.hr", out var credential));
        Assert.Equal("user:pass", credential);
    }

    /// <summary>The indexed setting beats a leftover in a config file: that is the one an operator set deliberately for this environment.</summary>
    [Fact]
    public void Indexed_entry_wins_over_the_map_for_the_same_host()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuth:b2b.promet-info.hr"] = "stale:stale",
            ["Feeds:BasicAuthHosts:0:Host"] = "b2b.promet-info.hr",
            ["Feeds:BasicAuthHosts:0:Credential"] = "fresh:fresh"
        }), Apps);

        Assert.Equal("fresh:fresh", result["b2b.promet-info.hr"]);
    }

    /// <summary>A half-filled entry is a deploy bug and fails loudly at load, rather than reading as "no credential configured".</summary>
    [Theory]
    [InlineData("b2b.promet-info.hr", null)]
    [InlineData(null, "user:pass")]
    [InlineData("b2b.promet-info.hr", "")]
    public void Throws_on_incomplete_indexed_entries(string? host, string? credential)
    {
        Assert.Throws<InvalidOperationException>(() => HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuthHosts:0:Host"] = host,
            ["Feeds:BasicAuthHosts:0:Credential"] = credential
        }), Apps));
    }

    [Fact]
    public void Missing_credential_is_detected_as_half_filled()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuthHosts:0:Host"] = "b2b.promet-info.hr",
            ["Feeds:BasicAuthHosts:0:Credential"] = null
        }), Apps));

        Assert.Contains("half-filled", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A host that is misspelt is still a host: it loads, and the mismatch shows at the request, not at load.</summary>
    [Fact]
    public void A_misspelt_host_with_its_credential_loads_as_its_own_host()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuthHosts:0:Host"] = "b2b.promet-info.hr-typo",
            ["Feeds:BasicAuthHosts:0:Credential"] = "user:pass"
        }), Apps);

        Assert.Single(result);
        Assert.True(result.ContainsKey("b2b.promet-info.hr-typo"));
    }

    [Fact]
    public void Matched_entry_is_used_for_host_and_a_non_matching_host_gets_none()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuthHosts:0:Host"] = "b2b.promet-info.hr",
            ["Feeds:BasicAuthHosts:0:Credential"] = "user:pass"
        }), Apps);

        Assert.True(result.TryGetValue("b2b.promet-info.hr", out var credential));
        Assert.Equal("user:pass", credential);
        Assert.False(result.TryGetValue("example.org", out _));
    }

    [Fact]
    public void Reads_several_hosts()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuthHosts:0:Host"] = "b2b.promet-info.hr",
            ["Feeds:BasicAuthHosts:0:Credential"] = "one:1",
            ["Feeds:BasicAuthHosts:1:Host"] = "example.org",
            ["Feeds:BasicAuthHosts:1:Credential"] = "two:2"
        }), Apps);

        Assert.Equal(2, result.Count);
        Assert.Equal("one:1", result["b2b.promet-info.hr"]);
        Assert.Equal("two:2", result["example.org"]);
    }

    [Fact]
    public void No_configuration_is_an_empty_map_not_a_failure()
    {
        Assert.Empty(HostCredentials.Load(Config([]), Apps));
    }

    [Fact]
    public void Without_a_map_section_only_the_indexed_list_is_read()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuth:b2b.promet-info.hr"] = "ignored:map",
            ["Partner:Hosts:0:Host"] = "example.org",
            ["Partner:Hosts:0:Credential"] = "two:2"
        }), new HostCredentialsOptions { SectionName = "Partner:Hosts" });

        var only = Assert.Single(result);
        Assert.Equal("example.org", only.Key);
    }

    [Fact]
    public void The_section_names_are_options_so_no_app_name_is_built_in()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Clinic:Creds:0:Host"] = "portal.example.test",
            ["Clinic:Creds:0:Credential"] = "nurse:pw",
        }), new HostCredentialsOptions { SectionName = "Clinic:Creds" });

        Assert.Equal("nurse:pw", result["portal.example.test"]);
    }

    [Fact]
    public void A_blank_section_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => HostCredentials.Load(Config([]), new HostCredentialsOptions { SectionName = " " }));
    }

    [Fact]
    public void A_blank_indexed_entry_is_ignored()
    {
        var result = HostCredentials.Load(Config(new()
        {
            ["Feeds:BasicAuthHosts:0:Host"] = " ",
            ["Feeds:BasicAuthHosts:0:Credential"] = " ",
        }), Apps);

        Assert.Empty(result);
    }
}
