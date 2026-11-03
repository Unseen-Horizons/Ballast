// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The problem codes of a directory sync. Each is listed for the owner and never applied; the subject says which person, role or principal.
/// The role-value codes are <see cref="MicrosoftRoleRules.RiderRoleProblem"/> and <see cref="MicrosoftRoleRules.BadRoleValueProblem"/>.
/// </summary>
public static class MicrosoftSyncProblems
{
    /// <summary>A group, or another principal that is not a user, has an app role. Its members are not read (that needs Entra ID P1).</summary>
    public const string GroupAssignment = "GROUP_ASSIGNMENT";

    /// <summary>A person has no first or last name in Microsoft. The account is left as it is.</summary>
    public const string NameMissing = "NAME_MISSING";

    /// <summary>A person has no user principal name, so there is no address to sign in with. The account is left as it is.</summary>
    public const string NoAddress = "NO_ADDRESS";

    /// <summary>The person's address is held by another account. The account keeps its old address.</summary>
    public const string AddressTaken = "ADDRESS_TAKEN";

    /// <summary>In the single-role mode, a person has more than one role for this console. The account is left as it is.</summary>
    public const string MultipleRoles = "MULTIPLE_ROLES";

    /// <summary>A role marked as Microsoft's no longer exists in Microsoft. It is listed, and never deleted: its permissions live here.</summary>
    public const string RoleNotInMicrosoft = "ROLE_NOT_IN_MICROSOFT";
}

/// <summary>A problem the directory sync found. <paramref name="Subject"/> is a person's address (or object id), a role name or an app role value.</summary>
/// <param name="Code">One of <see cref="MicrosoftSyncProblems"/>, or a role-value code from <see cref="MicrosoftRoleRules"/>.</param>
/// <param name="Subject">The person, role or value the problem is about.</param>
/// <param name="Detail">Extra text, or null.</param>
public sealed record MicrosoftSyncProblem(string Code, string Subject, string? Detail = null);

/// <summary>A role of this console, and whether it was created from Microsoft (its <c>source = microsoft</c> role claim).</summary>
/// <param name="Name">The role name.</param>
/// <param name="FromMicrosoft">True when the role was created by the sign-in or the sync.</param>
public sealed record MicrosoftLocalRole(string Name, bool FromMicrosoft);

/// <summary>An account of this console with a Microsoft login. <paramref name="SwitchedOff"/> is the sync's own switch-off.</summary>
/// <param name="UserId">The account's id, as the key's text.</param>
/// <param name="ObjectId">The person's Microsoft object id.</param>
/// <param name="Email">The account's address, or null.</param>
/// <param name="Roles">The account's roles.</param>
/// <param name="SwitchedOff">True when the sync switched the account off.</param>
public sealed record MicrosoftLinkedAccount(string UserId, string ObjectId, string? Email, IReadOnlyList<string> Roles, bool SwitchedOff);

/// <summary>One linked account to apply. <paramref name="Changed"/> is true when the address, the roles or the switch-on would change.</summary>
public sealed record MicrosoftSyncUpdate(string UserId, MicrosoftPerson Person, IReadOnlyList<string> Roles, bool Changed);

/// <summary>The counts one run reports.</summary>
/// <param name="UsersAssigned">The people with an app role of this application.</param>
/// <param name="AppRoles">The app role values of this application.</param>
/// <param name="Updated">The linked accounts whose address, roles or switch-on would change.</param>
/// <param name="SwitchedOff">The linked accounts the run switches off.</param>
public sealed record MicrosoftSyncCounts(int UsersAssigned, int AppRoles, int Updated, int SwitchedOff);

/// <summary>
/// What one run would do. The plan is decided before anything is written; <see cref="LooksWrong"/> stops the run.
/// </summary>
/// <param name="Updates">The linked accounts to apply.</param>
/// <param name="SwitchOffUserIds">The linked accounts to switch off.</param>
/// <param name="RolesToCreate">The console roles that do not exist yet.</param>
/// <param name="RolesNotInMicrosoft">The roles from Microsoft that are no longer in Microsoft. Listed, never deleted.</param>
/// <param name="Problems">What the owner should look at. Nothing listed is applied.</param>
/// <param name="Counts">The counts for the run.</param>
/// <param name="LooksWrong">True when the run would leave no linked account on, though some were on.</param>
public sealed record MicrosoftSyncPlanResult(
    IReadOnlyList<MicrosoftSyncUpdate> Updates,
    IReadOnlyList<string> SwitchOffUserIds,
    IReadOnlyList<string> RolesToCreate,
    IReadOnlyList<string> RolesNotInMicrosoft,
    IReadOnlyList<MicrosoftSyncProblem> Problems,
    MicrosoftSyncCounts Counts,
    bool LooksWrong);

/// <summary>
/// The directory sync's rules, pure: the snapshot and the accounts in, the changes and the problems out. Nothing is read or written here,
/// so each rule is tested alone.
/// <para>
/// The sync never creates an account: a person's account is made at their first sign-in, the only time there is a person to check against.
/// It never deletes a role, and it touches only accounts that have a Microsoft login. An emergency account (an id the app names through
/// <see cref="IMicrosoftEmergencyAccounts{TKey}"/>) is left out entirely: it is never switched off, never applied, and never counted
/// in <see cref="MicrosoftSyncPlanResult.LooksWrong"/>.
/// </para>
/// </summary>
public static class MicrosoftSyncPlan
{
    /// <summary>Decides what a run would do.</summary>
    /// <param name="snapshot">What Microsoft gave: the app roles, the assignments and the users.</param>
    /// <param name="options">The settings: the console prefix, the rider role and the role mode.</param>
    /// <param name="localRoles">This console's roles, and which were created from Microsoft.</param>
    /// <param name="linkedAccounts">This console's accounts with a Microsoft login.</param>
    /// <param name="addressOwners">Lower-case address to account id, for every account that holds an address Microsoft also has.</param>
    /// <param name="emergencyUserIds">The account ids, as text, of the app's emergency accounts. They are left out entirely.</param>
    /// <returns>The plan.</returns>
    public static MicrosoftSyncPlanResult Plan(
        MicrosoftDirectorySnapshot snapshot,
        MicrosoftSignInOptions options,
        IReadOnlyCollection<MicrosoftLocalRole> localRoles,
        IReadOnlyCollection<MicrosoftLinkedAccount> linkedAccounts,
        IReadOnlyDictionary<string, string> addressOwners,
        IReadOnlySet<string> emergencyUserIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(localRoles);
        ArgumentNullException.ThrowIfNull(linkedAccounts);
        ArgumentNullException.ThrowIfNull(addressOwners);
        ArgumentNullException.ThrowIfNull(emergencyUserIds);

        List<MicrosoftSyncProblem> problems = [];

        // The app roles by id. An assignment to an id that is not an app role is the default access: no role, and no problem.
        var roleValues = new Dictionary<Guid, string>();
        foreach (var role in snapshot.AppRoles)
        {
            roleValues[role.Id] = role.Value;
        }

        var roleValueList = roleValues.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // A group, or another principal that is not a user, is reported once and never applied.
        foreach (var principalId in snapshot.Assignments
                     .Where(a => a.PrincipalType != "User" && roleValues.ContainsKey(a.AppRoleId))
                     .Select(a => a.PrincipalId)
                     .Distinct())
        {
            problems.Add(new MicrosoftSyncProblem(MicrosoftSyncProblems.GroupAssignment, principalId.ToString("D")));
        }

        // The app role values of each user.
        var valuesByUser = new Dictionary<Guid, List<string>>();
        foreach (var assignment in snapshot.Assignments.Where(a => a.PrincipalType == "User"))
        {
            if (!roleValues.TryGetValue(assignment.AppRoleId, out var value))
            {
                continue;
            }

            if (!valuesByUser.TryGetValue(assignment.PrincipalId, out var values))
            {
                valuesByUser[assignment.PrincipalId] = values = [];
            }

            if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value);
            }
        }

        // A value that no console can use is reported once, for the app role, not once for each person who has it.
        foreach (var value in roleValueList)
        {
            problems.AddRange(MicrosoftRoleRules.ForConsole([value], options).Problems);
        }

        // The roles this console has, from every app role value. A role for another console is not an error here.
        var rolesForConsole = roleValueList
            .SelectMany(value => MicrosoftRoleRules.ForConsole([value], options).Roles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var consoleNames = rolesForConsole.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localNames = localRoles.Select(role => role.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rolesToCreate = rolesForConsole.Where(name => !localNames.Contains(name)).ToList();

        // A role that came from Microsoft and is no longer one is listed. It is not deleted.
        var rolesNotInMicrosoft = localRoles
            .Where(role => role.FromMicrosoft && !consoleNames.Contains(role.Name))
            .Select(role => role.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var name in rolesNotInMicrosoft)
        {
            problems.Add(new MicrosoftSyncProblem(MicrosoftSyncProblems.RoleNotInMicrosoft, name));
        }

        // The emergency accounts are outside the sync: they are not switched off, not applied and not counted.
        var accounts = linkedAccounts.Where(account => !emergencyUserIds.Contains(account.UserId)).ToList();
        var linked = accounts.ToDictionary(account => account.ObjectId, StringComparer.OrdinalIgnoreCase);

        List<MicrosoftSyncUpdate> updates = [];
        List<string> switchOffIds = [];

        // The object ids the directory has decided: updated, switched off, or left as they are.
        var decided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (principalId, values) in valuesByUser)
        {
            var objectId = principalId.ToString("D");
            decided.Add(objectId);
            linked.TryGetValue(objectId, out var account);

            // The reader gives every assigned user, so this does not happen. If it did, the account is not switched off on a read that did not say so.
            if (!snapshot.Users.TryGetValue(principalId, out var user))
            {
                continue;
            }

            var address = MicrosoftPersonReader.NormalizeAddress(user.UserPrincipalName);
            var label = address ?? objectId;

            // A person whose address is an emergency account's is left out, as the emergency account is: the address is not applied to.
            if (address is not null && addressOwners.TryGetValue(address, out var addressOwner) && emergencyUserIds.Contains(addressOwner))
            {
                continue;
            }

            var roles = MicrosoftRoleRules.ForConsole(values, options).Roles;

            // No role in this console, or disabled: the account is switched off when it is on.
            if (roles.Count == 0 || !user.AccountEnabled)
            {
                if (account is { SwitchedOff: false })
                {
                    switchOffIds.Add(account.UserId);
                }

                continue;
            }

            if (options.RoleMode == MicrosoftRoleMode.One && roles.Count > 1)
            {
                problems.Add(new MicrosoftSyncProblem(MicrosoftSyncProblems.MultipleRoles, label));
                continue;
            }

            var given = (user.GivenName ?? string.Empty).Trim();
            var surname = (user.Surname ?? string.Empty).Trim();
            if (given.Length == 0 || surname.Length == 0)
            {
                problems.Add(new MicrosoftSyncProblem(MicrosoftSyncProblems.NameMissing, label));
                continue;
            }

            if (address is null)
            {
                problems.Add(new MicrosoftSyncProblem(MicrosoftSyncProblems.NoAddress, label));
                continue;
            }

            // The address is held by another account. The account is still updated, except its address.
            var holder = addressOwners.TryGetValue(address, out var owner) ? owner : null;
            var addressTaken = holder is not null && (account is null || !string.Equals(holder, account.UserId, StringComparison.Ordinal));
            if (addressTaken)
            {
                problems.Add(new MicrosoftSyncProblem(MicrosoftSyncProblems.AddressTaken, address));
            }

            // A person with no account yet is not created here: the first sign-in makes it.
            if (account is null)
            {
                continue;
            }

            var addressChanged = !addressTaken
                && !string.Equals(MicrosoftPersonReader.NormalizeAddress(account.Email), address, StringComparison.Ordinal);
            var rolesChanged = !account.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(roles);
            var changed = addressChanged || rolesChanged || account.SwitchedOff;
            updates.Add(new MicrosoftSyncUpdate(
                account.UserId,
                new MicrosoftPerson(objectId, address, given, surname, values),
                roles,
                changed));
        }

        // A linked account whose person has no role here at all, or is not in the directory any more, is switched off.
        foreach (var account in accounts)
        {
            if (!decided.Contains(account.ObjectId) && !account.SwitchedOff)
            {
                switchOffIds.Add(account.UserId);
            }
        }

        // The run looks wrong when it would leave no linked account on, though some were on. Nothing is applied then.
        var updatedIds = updates.Select(update => update.UserId).ToHashSet(StringComparer.Ordinal);
        var switchOffSet = switchOffIds.ToHashSet(StringComparer.Ordinal);
        var onBefore = accounts.Count(account => !account.SwitchedOff);
        var onAfter = accounts.Count(account =>
            updatedIds.Contains(account.UserId) || (!account.SwitchedOff && !switchOffSet.Contains(account.UserId)));
        var looksWrong = onBefore > 0 && onAfter == 0;

        var counts = new MicrosoftSyncCounts(
            valuesByUser.Count,
            roleValueList.Count,
            updates.Count(update => update.Changed),
            switchOffIds.Count);

        return new MicrosoftSyncPlanResult(updates, switchOffIds, rolesToCreate, rolesNotInMicrosoft, problems, counts, looksWrong);
    }
}
