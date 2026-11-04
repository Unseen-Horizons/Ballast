// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs;

/// <summary>
/// Work that has to happen when this copy becomes the holder and before any holder-only worker is released:
/// the previous holder has finished stopping by then, so whatever it left behind can be dealt with safely.
/// <para>
/// Hooks run one after another with <c>IsHolder</c> still false and the heartbeat already running. A hook
/// resolves what it needs when it is called, not in its constructor, because the services it reaches may
/// themselves ask for <see cref="IJobLeadership"/>. An exception is logged at Error and the term goes on.
/// </para>
/// </summary>
public interface IJobTermHook
{
    /// <summary>Runs when this copy has taken the lock, before its holder-only workers start.</summary>
    /// <param name="ct">Cancelled when the term ends before the hook has finished.</param>
    Task OnTermStartAsync(CancellationToken ct);
}
