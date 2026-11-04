// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>The settings of <see cref="MicrosoftSyncStatus"/>.</summary>
public sealed class MicrosoftSyncStatusOptions
{
    /// <summary>
    /// How long before its end the client secret is reported as ending soon. Default 30 days. A secret that ends within this window reads
    /// <see cref="ClientSecretState.Soon"/>, and one that has ended reads <see cref="ClientSecretState.Expired"/>. Zero is allowed.
    /// </summary>
    public TimeSpan SecretWarningWindow { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Checks the options.</summary>
    /// <exception cref="InvalidOperationException"><see cref="SecretWarningWindow"/> is negative. The message names the setting.</exception>
    public void Validate()
    {
        if (SecretWarningWindow < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(SecretWarningWindow)} must not be negative.");
        }
    }
}
