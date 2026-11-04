// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;

namespace Ballast.Auth;

/// <summary>
/// The one address rule that is left when <see cref="AuthOptions.RequireUniqueEmail"/> is off. Identity checks an address only when it is
/// required and unique, so this check runs beside Identity's own: an account with no address is allowed, and an address that another account
/// already has is refused with Identity's <c>DuplicateEmail</c>. The lookup goes through the same <see cref="UserManager{TUser}"/> that runs the
/// validators, so its normalisation is Identity's and no service is resolved here. It is registered only when the switch is off.
/// </summary>
/// <typeparam name="TUser">The app's Identity user.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class AddressUniquenessValidator<TUser, TKey> : IUserValidator<TUser>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    /// <inheritdoc />
    public async Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return IdentityResult.Success;
        }

        var owner = await manager.FindByEmailAsync(user.Email);
        if (owner is not null && !EqualityComparer<TKey>.Default.Equals(owner.Id, user.Id))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateEmail",
                Description = $"Email '{user.Email}' is already taken.",
            });
        }

        return IdentityResult.Success;
    }
}
