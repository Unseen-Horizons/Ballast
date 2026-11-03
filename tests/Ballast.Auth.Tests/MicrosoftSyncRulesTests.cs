// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth.Tests;

/// <summary>The directory sync's pure rules and the sign-in options' checks. No database and no network: each rule is tested alone.</summary>
public class MicrosoftSyncRulesTests
{
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bo = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid AdminRoleId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OperatorRoleId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    private static MicrosoftSignInOptions Options(Action<MicrosoftSignInOptions>? configure = null)
    {
        var options = new MicrosoftSignInOptions
        {
            TenantId = "11111111-2222-3333-4444-555555555555",
            ClientId = "66666666-7777-8888-9999-000000000000",
            ClientSecret = "test-client-secret-not-real",
            ConsolePrefix = "Api",
        };
        configure?.Invoke(options);
        return options;
    }

    private static MicrosoftDirectorySnapshot Directory(
        IEnumerable<(Guid Id, string Value)> appRoles,
        IEnumerable<MicrosoftAssignment> assignments,
        params MicrosoftUser[] users) =>
        new(
            appRoles.Select(r => new MicrosoftAppRole(r.Id, r.Value)).ToList(),
            assignments.ToList(),
            users.ToDictionary(u => u.Id),
            null);

    private static MicrosoftUser User(Guid id, string address) => new(id, "Ana", "Horvat", address, true);

    private static MicrosoftLinkedAccount Linked(string userId, Guid objectId, string email, bool switchedOff = false) =>
        new(userId, objectId.ToString("D"), email, [], switchedOff);

    private static readonly IReadOnlyDictionary<string, string> NoOwners = new Dictionary<string, string>();

    private static readonly IReadOnlySet<string> NoEmergency = new HashSet<string>(StringComparer.Ordinal);

    private static HashSet<string> Ids(params string[] ids) => new(ids, StringComparer.Ordinal);

    [Fact]
    public void The_prefix_rule_reads_this_consoles_roles_and_reports_a_bare_prefix_and_the_rider_role()
    {
        var options = Options(o => o.RiderRole = "Rider");

        var reading = MicrosoftRoleRules.ForConsole(
            ["Api.Admin", "Map.Viewer", "Api.", "Rider", "Api.Operator", "api.admin", "Auditor"],
            options);

        // A value with the other console's prefix is that console's to judge: no role, no problem. A bare "Api." is a problem, and so is the rider role.
        Assert.Equal(new List<string> { "Admin", "Operator", "Auditor" }, reading.Roles);
        Assert.Equal(new List<string> { MicrosoftRoleRules.BadRoleValueProblem, MicrosoftRoleRules.RiderRoleProblem },
            reading.Problems.Select(p => p.Code));
    }

    [Fact]
    public void Without_a_console_prefix_every_value_is_a_role_name_as_it_is()
    {
        var reading = MicrosoftRoleRules.ForConsole(["Api.Admin", "Admin"], new MicrosoftSignInOptions());

        Assert.Equal(new List<string> { "Api.Admin", "Admin" }, reading.Roles);
        Assert.Empty(reading.Problems);
    }

    [Theory]
    [InlineData("ana@example.test", true)]
    [InlineData("ANA@Example.TEST", true)]
    [InlineData("ana@example.test.evil.net", false)]
    [InlineData("ana@other.test", false)]
    [InlineData("ana@", false)]
    [InlineData("", false)]
    public void The_domain_check_admits_only_the_company_domains(string address, bool allowed)
    {
        var options = Options(o => o.AllowedDomains = ["example.test"]);

        Assert.Equal(allowed, options.IsCompanyAddress(address));
    }

    [Fact]
    public void With_no_company_domain_every_address_passes_the_domain_check()
    {
        Assert.True(Options().IsCompanyAddress("anyone@anywhere.test"));
    }

    [Fact]
    public void Values_that_cannot_be_used_are_refused_at_start()
    {
        Assert.Throws<InvalidOperationException>(() => new MicrosoftSignInOptions { TenantId = "not-a-guid" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new MicrosoftSignInOptions { SyncMinutes = 4 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new MicrosoftSignInOptions { ConsolePrefix = "Foo" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new MicrosoftSignInOptions { ConsolePrefix = "Api." }.Validate());
        Assert.Throws<InvalidOperationException>(() => new MicrosoftSignInOptions { CallbackPath = "signin-oidc" }.Validate());
    }

    [Fact]
    public void A_run_that_would_switch_off_every_linked_account_looks_wrong_and_switches_none_off_by_itself()
    {
        // The directory has no assignments at all: a wrong tenant or an empty assignment list looks just like this.
        var directory = Directory([(AdminRoleId, "Api.Admin")], []);
        var linked = new[] { Linked("user-ana", Ana, "ana@example.test") };

        var plan = MicrosoftSyncPlan.Plan(directory, Options(), [], linked, NoOwners, NoEmergency);

        Assert.True(plan.LooksWrong);
        Assert.Equal(new List<string> { "user-ana" }, plan.SwitchOffUserIds);
    }

    [Fact]
    public void A_run_that_keeps_one_account_on_is_not_looks_wrong()
    {
        var directory = Directory(
            [(AdminRoleId, "Api.Admin")],
            [new MicrosoftAssignment(Ana, "User", AdminRoleId)],
            User(Ana, "ana@example.test"));
        var linked = new[]
        {
            Linked("user-ana", Ana, "ana@example.test"),
            Linked("user-bo", Bo, "bo@example.test"),
        };

        var plan = MicrosoftSyncPlan.Plan(directory, Options(), [], linked, NoOwners, NoEmergency);

        Assert.False(plan.LooksWrong);
        Assert.Equal(new List<string> { "user-bo" }, plan.SwitchOffUserIds);
    }

    [Fact]
    public void An_emergency_account_is_never_switched_off_and_does_not_count_toward_looks_wrong()
    {
        var directory = Directory([(AdminRoleId, "Api.Admin")], []);
        var emergency = new[] { Linked("user-emergency", Ana, "EMERGENCY@example.test") };

        var plan = MicrosoftSyncPlan.Plan(directory, Options(), [], emergency, NoOwners, Ids("user-emergency"));

        // The only linked account is the emergency one, so there is nothing on that the run could switch off.
        Assert.False(plan.LooksWrong);
        Assert.Empty(plan.SwitchOffUserIds);
        Assert.Empty(plan.Updates);
    }

    [Fact]
    public void An_emergency_account_is_left_out_of_a_run_that_switches_a_staff_account_off()
    {
        var directory = Directory([(AdminRoleId, "Api.Admin")], []);
        var linked = new[]
        {
            Linked("user-staff", Bo, "bo@example.test"),
            Linked("user-emergency", Ana, "emergency@example.test"),
        };

        var plan = MicrosoftSyncPlan.Plan(directory, Options(), [], linked, NoOwners, Ids("user-emergency"));

        // Only the staff account is counted, so this run would leave nothing on and is refused as a whole.
        Assert.True(plan.LooksWrong);
        Assert.DoesNotContain("user-emergency", plan.SwitchOffUserIds);
    }

    [Fact]
    public void A_person_whose_address_is_an_emergency_accounts_is_left_out_without_a_problem()
    {
        var directory = Directory(
            [(AdminRoleId, "Api.Admin")],
            [new MicrosoftAssignment(Ana, "User", AdminRoleId)],
            User(Ana, "emergency@example.test"));
        var owners = new Dictionary<string, string> { ["emergency@example.test"] = "user-emergency" };

        var plan = MicrosoftSyncPlan.Plan(directory, Options(), [], [], owners, Ids("user-emergency"));

        // The address is the emergency account's, so the person is left out as that account is: nothing is applied and nothing is listed.
        Assert.Empty(plan.Updates);
        Assert.Empty(plan.SwitchOffUserIds);
        Assert.Empty(plan.Problems);

        // Without the hook the same person is listed, so the hook is what leaves them out.
        var unguarded = MicrosoftSyncPlan.Plan(directory, Options(), [], [], owners, NoEmergency);
        Assert.Contains(unguarded.Problems, p => p.Code == MicrosoftSyncProblems.AddressTaken);
    }

    [Fact]
    public void In_single_role_mode_a_person_with_two_roles_is_left_alone_and_reported()
    {
        var options = Options(o => o.RoleMode = MicrosoftRoleMode.One);
        var directory = Directory(
            [(AdminRoleId, "Api.Admin"), (OperatorRoleId, "Api.Operator")],
            [
                new MicrosoftAssignment(Ana, "User", AdminRoleId),
                new MicrosoftAssignment(Ana, "User", OperatorRoleId),
            ],
            User(Ana, "ana@example.test"));
        var linked = new[] { Linked("user-ana", Ana, "ana@example.test") };

        var plan = MicrosoftSyncPlan.Plan(directory, options, [], linked, NoOwners, NoEmergency);

        Assert.Contains(plan.Problems, p => p.Code == MicrosoftSyncProblems.MultipleRoles && p.Subject == "ana@example.test");
        Assert.Empty(plan.Updates);
        Assert.Empty(plan.SwitchOffUserIds);
        Assert.False(plan.LooksWrong);
    }

    [Fact]
    public void A_role_created_from_microsoft_that_microsoft_no_longer_has_is_listed_and_never_deleted()
    {
        var directory = Directory([(AdminRoleId, "Api.Admin")], []);
        var local = new[] { new MicrosoftLocalRole("Admin", true), new MicrosoftLocalRole("Retired", true), new MicrosoftLocalRole("Owner", false) };

        var plan = MicrosoftSyncPlan.Plan(directory, Options(), local, [], NoOwners, NoEmergency);

        Assert.Equal(new List<string> { "Retired" }, plan.RolesNotInMicrosoft);
        Assert.Contains(plan.Problems, p => p.Code == MicrosoftSyncProblems.RoleNotInMicrosoft && p.Subject == "Retired");
        Assert.DoesNotContain(plan.Problems, p => p.Subject == "Owner");
    }
}
