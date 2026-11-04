// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Auth;

/// <summary>What the app does when the session ends. Both hooks are the app's own sign-out work: the library only says when.</summary>
public interface ISessionHooks
{
    /// <summary>
    /// The library has to drop the tokens because the server's refresh reply could not be read, so the replacement is lost. It runs
    /// before the tokens are cleared: the app may still need them (an owner key derived from the stored token is wiped here).
    /// </summary>
    Task TokensDroppedAsync(CancellationToken ct = default);

    /// <summary>The server refused the refresh token on a request. The app signs the rider out here.</summary>
    Task SignedOutAsync(CancellationToken ct = default);
}

/// <summary>Hooks that do nothing, for an app with no sign-out work of its own, and for tests.</summary>
public sealed class NoSessionHooks : ISessionHooks
{
    /// <inheritdoc />
    public Task TokensDroppedAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SignedOutAsync(CancellationToken ct = default) => Task.CompletedTask;
}
