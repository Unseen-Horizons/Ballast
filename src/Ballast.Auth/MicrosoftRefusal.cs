// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The refusals of Microsoft staff sign-in. Each is the <c>error</c> query of the redirect to the sign-in page, so the page shows the matching
/// sentence. A refusal is never an account: the person gets no session. The one-time-code refusal is <see cref="AuthFailures.MicrosoftCodeInvalid"/>
/// instead.
/// </summary>
public static class MicrosoftRefusal
{
    /// <summary>Microsoft sign-in is not set up: the tenant or the client is empty.</summary>
    public const string NotConfigured = "MS_NOT_CONFIGURED";

    /// <summary>The address typed on the sign-in page is not at one of the company domains.</summary>
    public const string NotCompanyAddress = "MS_NOT_COMPANY_ADDRESS";

    /// <summary>The handshake failed, or the ID token lacks a usable object id or address.</summary>
    public const string SignInFailed = "MS_SIGNIN_FAILED";

    /// <summary>The account belongs to another tenant than the one configured.</summary>
    public const string WrongTenant = "MS_WRONG_TENANT";

    /// <summary>The person has no role for this console.</summary>
    public const string NoRole = "MS_NO_ROLE";

    /// <summary>The person has more than one role for this console, in the single-role mode.</summary>
    public const string MultipleRoles = "MS_MULTIPLE_ROLES";

    /// <summary>The token lacks the given name or the family name. Names are never split out of the display name.</summary>
    public const string NameMissing = "MS_NAME_MISSING";

    /// <summary>The role exists, but no permission is set on it yet, so the person gets no session until it is.</summary>
    public const string RoleNoPermissions = "MS_ROLE_NO_PERMISSIONS";

    /// <summary>The address belongs to a password account. It is never linked, so the Microsoft account is refused.</summary>
    public const string AddressTaken = "MS_ADDRESS_TAKEN";

    /// <summary>The Microsoft login belongs to an emergency account. A sign-in never links or changes one, so it is refused.</summary>
    public const string EmergencyAccount = "MS_EMERGENCY_ACCOUNT";
}
