// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;

namespace Ballast.Admin.Tests;

public class AdminWhoAmITests
{
    [Fact]
    public void A_principal_that_is_not_signed_in_gives_null()
    {
        Assert.Null(AdminWhoAmI.Create(new ClaimsPrincipal()));
    }

    [Fact]
    public void An_identity_without_an_authentication_type_is_not_signed_in_even_with_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "u-1"), new Claim("role", "Admin") }));

        Assert.Null(AdminWhoAmI.Create(principal));
    }

    [Fact]
    public void A_signed_in_person_gives_the_subject_name_email_roles_and_permissions()
    {
        var principal = SignedIn(
            new Claim("sub", "u-1"),
            new Claim("name", "Ana"),
            new Claim("email", "ana@example.test"),
            new Claim("role", "Reader"),
            new Claim("role", "Admin"),
            new Claim("role", "Admin"),
            new Claim("role", " "),
            new Claim("permission", "zeta.write"),
            new Claim("permission", "alpha.read"),
            new Claim("permission", "alpha.read"));

        var me = AdminWhoAmI.Create(principal);

        Assert.NotNull(me);
        Assert.Equal("u-1", me.Subject);
        Assert.Equal("Ana", me.Name);
        Assert.Equal("ana@example.test", me.Email);
        Assert.Equal("Admin, Reader", string.Join(", ", me.Roles));
        Assert.Equal("alpha.read, zeta.write", string.Join(", ", me.Permissions));
        Assert.False(me.FullAccess);
    }

    [Fact]
    public void A_role_that_is_the_full_access_role_is_matched_without_case()
    {
        var principal = SignedIn(new Claim("role", "Admin"));

        var me = AdminWhoAmI.Create(principal, new AdminWhoAmIOptions { FullAccessRole = "admin" });

        Assert.True(me!.FullAccess);
    }

    [Fact]
    public void Other_roles_do_not_give_full_access()
    {
        var principal = SignedIn(new Claim("role", "Reader"));

        var me = AdminWhoAmI.Create(principal, new AdminWhoAmIOptions { FullAccessRole = "Admin" });

        Assert.False(me!.FullAccess);
    }

    [Fact]
    public void Without_a_full_access_role_no_one_has_full_access()
    {
        var principal = SignedIn(new Claim("role", "Admin"));

        Assert.False(AdminWhoAmI.Create(principal)!.FullAccess);
    }

    [Fact]
    public void A_signed_in_person_with_no_claims_gives_null_fields_and_empty_lists()
    {
        var me = AdminWhoAmI.Create(SignedIn());

        Assert.NotNull(me);
        Assert.Null(me.Subject);
        Assert.Null(me.Name);
        Assert.Null(me.Email);
        Assert.Empty(me.Roles);
        Assert.Empty(me.Permissions);
        Assert.False(me.FullAccess);
    }

    [Fact]
    public void Blank_claim_values_are_ignored_for_the_subject_name_and_email()
    {
        var me = AdminWhoAmI.Create(SignedIn(new Claim("sub", " "), new Claim("name", ""), new Claim("email", "\t")));

        Assert.Null(me!.Subject);
        Assert.Null(me.Name);
        Assert.Null(me.Email);
    }

    [Fact]
    public void Roles_and_permissions_are_sorted_ordinally_so_capitals_come_first()
    {
        var principal = SignedIn(new Claim("permission", "b"), new Claim("permission", "B"), new Claim("permission", "a"));

        var me = AdminWhoAmI.Create(principal);

        Assert.Equal("B, a, b", string.Join(", ", me!.Permissions));
    }

    [Fact]
    public void The_claim_types_come_from_the_options()
    {
        var principal = SignedIn(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("role", "ignored"),
            new Claim("access", "staff.read"));

        var me = AdminWhoAmI.Create(principal, new AdminWhoAmIOptions
        {
            RoleClaim = ClaimTypes.Role,
            PermissionClaim = "access",
            FullAccessRole = "Admin",
        });

        Assert.Equal("Admin", string.Join(", ", me!.Roles));
        Assert.Equal("staff.read", string.Join(", ", me.Permissions));
        Assert.True(me.FullAccess);
    }

    [Theory]
    [InlineData(nameof(AdminWhoAmIOptions.SubjectClaim))]
    [InlineData(nameof(AdminWhoAmIOptions.NameClaim))]
    [InlineData(nameof(AdminWhoAmIOptions.EmailClaim))]
    [InlineData(nameof(AdminWhoAmIOptions.RoleClaim))]
    [InlineData(nameof(AdminWhoAmIOptions.PermissionClaim))]
    public void A_blank_claim_type_is_refused(string property)
    {
        var options = new AdminWhoAmIOptions();
        switch (property)
        {
            case nameof(AdminWhoAmIOptions.SubjectClaim): options.SubjectClaim = " "; break;
            case nameof(AdminWhoAmIOptions.NameClaim): options.NameClaim = " "; break;
            case nameof(AdminWhoAmIOptions.EmailClaim): options.EmailClaim = " "; break;
            case nameof(AdminWhoAmIOptions.RoleClaim): options.RoleClaim = " "; break;
            default: options.PermissionClaim = " "; break;
        }

        var ex = Assert.Throws<ArgumentException>(() => AdminWhoAmI.Create(SignedIn(), options));
        Assert.Equal(property, ex.ParamName);
    }

    [Fact]
    public void A_null_principal_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => AdminWhoAmI.Create(null!));
    }

    private static ClaimsPrincipal SignedIn(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
