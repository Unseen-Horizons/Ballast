// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>The roles one console takes from a person's role values, and the problems the values raise.</summary>
/// <param name="Roles">The console's role names, distinct, in their own case.</param>
/// <param name="Problems">The values that cannot be used, each with its code.</param>
public sealed record MicrosoftRoleReading(IReadOnlyList<string> Roles, IReadOnlyList<MicrosoftSyncProblem> Problems);

/// <summary>
/// Reads one console's roles from the Microsoft role values. With a console prefix, <c>Prefix.Name</c> is this console's role <c>Name</c>, a value
/// with another console's prefix is left to that console, and a bare <c>Prefix.</c> is a problem. A value with no known prefix is a role name
/// as it is. The rider role is a problem, never a staff role.
/// </summary>
public static class MicrosoftRoleRules
{
    /// <summary>The problem for a value that names the rider role.</summary>
    public const string RiderRoleProblem = "RIDER_ROLE";

    /// <summary>The problem for a value that is only a console prefix with nothing after the dot.</summary>
    public const string BadRoleValueProblem = "BAD_ROLE_VALUE";

    /// <summary>Reads the console's roles from the values.</summary>
    /// <param name="values">The app role values of one person, or of one app role.</param>
    /// <param name="options">The settings: the console prefix, the known prefixes and the rider role.</param>
    /// <returns>The roles and the problems.</returns>
    public static MicrosoftRoleReading ForConsole(IEnumerable<string> values, MicrosoftSignInOptions options)
    {
        List<string> roles = [];
        List<MicrosoftSyncProblem> problems = [];

        foreach (var raw in values)
        {
            var value = raw.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            var name = value;
            var dot = value.IndexOf('.');
            var prefix = dot > 0 ? value[..dot] : null;
            if (options.ConsolePrefix is not null && prefix is not null && options.ConsolePrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                // Only the part before the first dot is the prefix; the rest is the role name, in its own case.
                name = value[(dot + 1)..].Trim();
                if (name.Length == 0)
                {
                    problems.Add(new MicrosoftSyncProblem(BadRoleValueProblem, value));
                    continue;
                }

                // Another console's value is that console's to judge, so it is skipped without a problem.
                if (!string.Equals(prefix, options.ConsolePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (options.RiderRole is not null && string.Equals(name, options.RiderRole, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new MicrosoftSyncProblem(RiderRoleProblem, value));
                continue;
            }

            if (!roles.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                roles.Add(name);
            }
        }

        return new MicrosoftRoleReading(roles, problems);
    }
}
