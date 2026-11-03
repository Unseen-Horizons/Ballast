// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

using Ballast.Testing.Guards;
using Ballast.Testing.Guards.EndpointAuth;

namespace Ballast.Testing.Tests.Guards;

/// <summary>The endpoint-authorization rules: the resolver against the fixtures, the table comparison, policies, duplicates, the inventory and the startup code.</summary>
public class EndpointAuthGuardTests
{
    private static readonly Assembly Own = typeof(EndpointAuthGuardTests).Assembly;

    private static readonly EndpointAuthOptions Marked = new() { AppSessionMarker = "EpMarkerAttribute" };

    private static MethodInfo Method(Type type, string name) => type.GetMethod(name)!;

    [Fact]
    public void Discovery_finds_each_public_action_with_its_route_and_skips_the_non_action()
    {
        var found = EndpointAuthGuard.Discover(Own, Marked)
            .Where(endpoint => endpoint.Key.StartsWith("Ep", StringComparison.Ordinal))
            .Select(endpoint => endpoint.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        string[] expected =
        [
            "EpMarkedController.Blocked", "EpMarkedController.Data", "EpNoneController.Nothing",
            "EpSecureController.Admin", "EpSecureController.Get", "EpSecureController.Open",
        ];
        Assert.Equal(expected, found);
        Assert.Equal("GET /epsecure/orders/{id}", EndpointAuthGuard.Discover(Own).Single(endpoint => endpoint.Key == "EpSecureController.Get").Route);
    }

    [Fact]
    public void A_class_level_authorize_is_signed_in_and_an_action_policy_is_named()
    {
        Assert.Equal(EndpointAuthGuard.Authenticated, EndpointAuthGuard.ResolveAuth(typeof(EpSecureController), Method(typeof(EpSecureController), "Get")));
        Assert.Equal("Policy:orders.manage", EndpointAuthGuard.ResolveAuth(typeof(EpSecureController), Method(typeof(EpSecureController), "Admin")));
    }

    [Fact]
    public void An_action_level_anonymous_opts_out_of_a_class_level_authorize()
    {
        Assert.Equal(EndpointAuthGuard.Anonymous, EndpointAuthGuard.ResolveAuth(typeof(EpSecureController), Method(typeof(EpSecureController), "Open")));
    }

    [Fact]
    public void An_action_with_no_attribute_anywhere_is_unprotected_and_never_assumed_open()
    {
        Assert.Equal(EndpointAuthGuard.Unprotected, EndpointAuthGuard.ResolveAuth(typeof(EpNoneController), Method(typeof(EpNoneController), "Nothing")));
    }

    [Fact]
    public void The_app_session_marker_without_anonymous_is_blocked_and_with_anonymous_is_app_or_user()
    {
        Assert.Equal(
            EndpointAuthGuard.AppOrUserBlockedByAuthorize,
            EndpointAuthGuard.ResolveAuth(typeof(EpMarkedController), Method(typeof(EpMarkedController), "Blocked"), Marked));
        Assert.Equal(EndpointAuthGuard.AppOrUser, EndpointAuthGuard.ResolveAuth(typeof(EpMarkedController), Method(typeof(EpMarkedController), "Data"), Marked));
    }

    [Fact]
    public void The_marker_name_is_an_option_and_an_unnamed_marker_is_not_read_as_one()
    {
        Assert.Equal(
            EndpointAuthGuard.Authenticated,
            EndpointAuthGuard.ResolveAuth(typeof(EpMarkedController), Method(typeof(EpMarkedController), "Blocked")));
    }

    [Fact]
    public void A_complete_table_has_no_problems()
    {
        var actual = new[] { new DiscoveredEndpoint("C.Same", "GET /c/same", EndpointAuthGuard.Authenticated) };
        var expected = new Dictionary<string, string>(StringComparer.Ordinal) { ["C.Same"] = EndpointAuthGuard.Authenticated };

        Assert.Empty(EndpointAuthGuard.Compare(actual, expected, "T"));
    }

    [Fact]
    public void Compare_reports_an_unclassified_a_changed_a_stale_and_an_unprotected_entry()
    {
        var actual = new List<DiscoveredEndpoint>
        {
            new("C.New", "GET /c/new", EndpointAuthGuard.Anonymous),
            new("C.Changed", "GET /c/changed", EndpointAuthGuard.Anonymous),
            new("C.Same", "GET /c/same", EndpointAuthGuard.Authenticated),
        };
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["C.Changed"] = EndpointAuthGuard.Authenticated,
            ["C.Same"] = EndpointAuthGuard.Authenticated,
            ["C.Gone"] = EndpointAuthGuard.Authenticated,
            ["C.Open"] = EndpointAuthGuard.Unprotected,
        };

        var problems = EndpointAuthGuard.Compare(actual, expected, "T");

        Assert.Contains(problems, p => p == "UNCLASSIFIED C.New (GET /c/new) resolves to Anonymous: add \"C.New\" to T with the auth it is meant to have.");
        Assert.Contains(problems, p => p.StartsWith("CHANGED C.Changed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p == "STALE C.Gone is in T but no such action exists any more: remove the entry.");
        Assert.Contains(problems, p => p.StartsWith("UNPROTECTED C.Open", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("C.Same", StringComparison.Ordinal));
    }

    [Fact]
    public void An_undefined_policy_is_reported_and_an_exempt_or_defined_one_is_not()
    {
        var problems = EndpointAuthGuard.UndefinedPolicyProblems(
            "ApiOne",
            ["Policy:orders.manage", "Policy:ghost+Roles:Admin", "Policy:routing"],
            ["orders.manage"],
            ["routing"]);

        Assert.Equal("ApiOne names an undefined policy 'ghost'.", Assert.Single(problems));
    }

    [Fact]
    public void Two_actions_sharing_a_key_or_a_route_are_reported()
    {
        var sharedKey = new[] { new DiscoveredEndpoint("C.A", "GET /a", "x"), new DiscoveredEndpoint("C.A", "GET /b", "x") };
        var sharedRoute = new[] { new DiscoveredEndpoint("C.A", "GET /x", "x"), new DiscoveredEndpoint("C.B", "get /X", "x") };

        Assert.Equal("Overloaded actions share a table key: C.A.", Assert.Single(EndpointAuthGuard.DuplicateProblems(sharedKey)));
        Assert.Contains("Two actions answer the same verb and route", Assert.Single(EndpointAuthGuard.DuplicateProblems(sharedRoute)), StringComparison.Ordinal);
        Assert.Empty(EndpointAuthGuard.DuplicateProblems(new[] { new DiscoveredEndpoint("C.A", "GET /a", "x") }));
    }

    [Fact]
    public void The_documented_inventory_matches_when_the_routes_agree_and_names_each_difference()
    {
        var actual = new[]
        {
            new DiscoveredEndpoint("C.Open", "GET /open", EndpointAuthGuard.Anonymous),
            new DiscoveredEndpoint("C.Secret", "GET /secret", EndpointAuthGuard.Anonymous),
        };

        var problem = Assert.Single(EndpointAuthGuard.RouteInventoryProblems(actual, EndpointAuthGuard.Anonymous, ["GET /open", "GET /gone"], "Anonymous endpoints"));

        Assert.Contains("In code but not documented: [get /secret]", problem, StringComparison.Ordinal);
        Assert.Contains("Documented but not in code: [get /gone]", problem, StringComparison.Ordinal);
        Assert.Empty(EndpointAuthGuard.RouteInventoryProblems(actual.Take(1), EndpointAuthGuard.Anonymous, ["GET /open"], "Anonymous endpoints"));
    }

    [Fact]
    public void An_app_session_action_that_is_not_anonymous_is_reported()
    {
        var blocked = EndpointAuthGuard.Discover(Own, Marked).Single(endpoint => endpoint.Key == "EpMarkedController.Blocked");

        Assert.Single(EndpointAuthGuard.AppSessionBlockedProblems(EndpointAuthGuard.Discover(Own, Marked)));
        Assert.Equal(EndpointAuthGuard.AppOrUserBlockedByAuthorize, blocked.Auth);
    }

    [Fact]
    public void A_startup_that_maps_only_the_health_call_has_no_problems()
    {
        Assert.Empty(EndpointAuthGuard.MinimalRouteProblems("var app = builder.Build();\napp.MapBallastHealth();"));
    }

    [Fact]
    public void A_minimal_route_a_second_health_call_and_an_anonymous_call_are_each_reported()
    {
        var mapped = Assert.Single(EndpointAuthGuard.MinimalRouteProblems("app.MapGet(\"/x\", () => 1);\napp.MapBallastHealth();"));
        Assert.StartsWith("Routes are mapped in the startup code, outside the authorization matrix: app.MapGet(", mapped, StringComparison.Ordinal);

        Assert.Equal(
            "app.MapBallastHealth() must be called exactly once; the startup code calls it 0 time(s).",
            Assert.Single(EndpointAuthGuard.MinimalRouteProblems("var app = builder.Build();")));
        Assert.Equal(
            "app.MapBallastHealth() must be called exactly once; the startup code calls it 2 time(s).",
            Assert.Single(EndpointAuthGuard.MinimalRouteProblems("app.MapBallastHealth();\napp.MapBallastHealth();")));
        var anonymous = EndpointAuthGuard.MinimalRouteProblems("app.MapBallastHealth();\napp.MapGroup(\"/g\").AllowAnonymous();");
        Assert.Equal(
            "The startup code calls .AllowAnonymous(), which opens a route outside the matrix.",
            Assert.Single(anonymous, p => p.Contains("AllowAnonymous", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_planted_route_is_caught_and_the_health_call_alone_passes()
    {
        GuardSelfTest.AssertCatches(source => EndpointAuthGuard.MinimalRouteProblems(source), "app.MapGet(\"/x\", () => 1);\napp.MapBallastHealth();", "Routes are mapped");
        GuardSelfTest.AssertPasses(source => EndpointAuthGuard.MinimalRouteProblems(source), "app.MapBallastHealth();");
    }

    [Fact]
    public void A_startup_check_that_reads_another_variable_fails_its_own_self_test()
    {
        var blind = new EndpointAuthOptions { AppVariable = "application" };

        Assert.ThrowsAny<Exception>(() =>
            GuardSelfTest.AssertCatches(source => EndpointAuthGuard.MinimalRouteProblems(source, blind), "app.MapGet(\"/x\", () => 1);\napp.MapBallastHealth();", "Routes are mapped"));
    }
}
