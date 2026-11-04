// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ballast.Auth;

/// <summary>Registers <see cref="MicrosoftSyncStatus"/> in an app.</summary>
public static class MicrosoftSyncStatusRegistration
{
    /// <summary>
    /// Adds the sync status as a singleton. The clock it reads is the app's <see cref="TimeProvider"/> when one is registered, otherwise the
    /// system clock.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">Sets the options. Null for the defaults.</param>
    /// <returns>The same services.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The options are out of range. See <see cref="MicrosoftSyncStatusOptions.Validate"/>.</exception>
    public static IServiceCollection AddBallastMicrosoftSyncStatus(
        this IServiceCollection services,
        Action<MicrosoftSyncStatusOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MicrosoftSyncStatusOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(sp => new MicrosoftSyncStatus(options, sp.GetService<TimeProvider>()));
        return services;
    }
}
