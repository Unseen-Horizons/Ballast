// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Platform;

/// <summary>
/// Where this install came from. The platform implementation asks the operating system, so the update notice can ask the server about the right
/// channel. The channel names are those of <see cref="Ballast.AppUpdates.InstallChannels"/>.
/// </summary>
public interface IInstallChannel
{
    /// <summary>
    /// One of <see cref="Ballast.AppUpdates.InstallChannels"/>, or null when it could not be told (the implementation logs why). A null channel is
    /// never replaced by a guess: the update check is skipped and shows nothing.
    /// </summary>
    string? Channel { get; }
}

/// <summary>A channel that is the same for every install of the platform (iOS, Windows).</summary>
public sealed class FixedInstallChannel : IInstallChannel
{
    /// <summary>Creates the channel.</summary>
    /// <param name="channel">The one channel every install of the platform reports.</param>
    public FixedInstallChannel(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        Channel = channel;
    }

    /// <inheritdoc />
    public string? Channel { get; }
}
