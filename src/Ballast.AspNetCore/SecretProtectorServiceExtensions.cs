// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballast.AspNetCore;

/// <summary>Registers the <see cref="SecretProtector"/>.</summary>
public static class SecretProtectorServiceExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="SecretProtector"/> with the given purpose. The data-protection key ring is the
    /// application's own: this helper does not configure it and does not call <c>AddDataProtection</c>.
    /// </summary>
    /// <param name="services">The services to add to. Required.</param>
    /// <param name="purpose">The data-protection purpose. The application's exact purpose string, kept across releases.</param>
    /// <param name="unreadableMode">What <see cref="SecretProtector.Unprotect"/> does with an unreadable credential.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unreadableMode"/> is not a defined value.</exception>
    public static IServiceCollection AddSecretProtector(
        this IServiceCollection services,
        string purpose,
        SecretUnreadableMode unreadableMode = SecretUnreadableMode.Throw)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (!Enum.IsDefined(unreadableMode))
        {
            throw new ArgumentOutOfRangeException(nameof(unreadableMode), unreadableMode, "The unreadable mode is not a defined value.");
        }

        var options = new SecretProtectorOptions { Purpose = purpose, UnreadableMode = unreadableMode };
        services.AddSingleton(sp => new SecretProtector(
            sp.GetRequiredService<IDataProtectionProvider>(),
            sp.GetRequiredService<ILogger<SecretProtector>>(),
            options));
        return services;
    }
}
