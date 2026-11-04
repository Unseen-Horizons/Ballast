// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;

namespace Ballast.AppAttestation;

/// <summary>
/// The nonces the bootstrap hands out. A nonce is single use and expires after <see cref="AppSessionOptions.NonceLifetime"/>.
/// Play Integrity binds a token to the nonce it was requested with, so a token captured from one install cannot be replayed to
/// mint a second session.
/// <para>
/// The library ships <see cref="InMemoryAppSessionNonceStore"/>. An app that runs more than one copy of its API needs a store every
/// copy shares, and registers its own implementation before <see cref="AppAttestationRegistration.AddAppAttestation"/>, which keeps any
/// store already registered.
/// </para>
/// </summary>
public interface IAppSessionNonceStore
{
    /// <summary>Stores a nonce that answers once, for <paramref name="lifetime"/>.</summary>
    /// <param name="nonce">The nonce. It is never reused while it is stored.</param>
    /// <param name="lifetime">How long it answers. Longer than zero.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>True when it was stored. False when the store is full: the caller refuses, and never evicts a live entry to make room.</returns>
    ValueTask<bool> AddAsync(string nonce, TimeSpan lifetime, CancellationToken ct = default);

    /// <summary>Removes a nonce and says whether it was stored and has not expired. A nonce answers once, so a second take is false.</summary>
    /// <param name="nonce">The nonce a client sent.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>True when the nonce was live, and it is now spent.</returns>
    ValueTask<bool> TakeAsync(string nonce, CancellationToken ct = default);
}

/// <summary>
/// The web tickets that carry a session from the app into its web view. A ticket is single use and expires after
/// <see cref="AppSessionOptions.TicketLifetime"/>. The ticket goes in a URL the web view navigates to, so it must be worthless
/// a minute later and after its first use.
/// </summary>
public interface IAppSessionTicketStore
{
    /// <summary>Stores a ticket that answers once, for <paramref name="lifetime"/>.</summary>
    /// <param name="ticket">The ticket.</param>
    /// <param name="grant">What the ticket stands for: the session token and when it ends.</param>
    /// <param name="lifetime">How long it answers. Longer than zero.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>True when it was stored. False when the store is full.</returns>
    ValueTask<bool> AddAsync(string ticket, WebTicketGrant grant, TimeSpan lifetime, CancellationToken ct = default);

    /// <summary>Removes a ticket and returns what it stood for, if it was stored and has not expired.</summary>
    /// <param name="ticket">The ticket a client sent.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The grant, or null when the ticket was never issued, was used already, or has expired.</returns>
    ValueTask<WebTicketGrant?> TakeAsync(string ticket, CancellationToken ct = default);
}

/// <summary>
/// The default nonce store: a bounded dictionary in this process's memory.
/// <para>
/// It holds only within one process. Two copies of the API during a deploy do not share it, so a nonce issued by one copy
/// cannot be spent at the other, and a session started on one copy can fail on the other with the same answer as an unknown
/// nonce. Use a store that every copy shares when that matters (see the usage guide, "Stores").
/// </para>
/// </summary>
public sealed class InMemoryAppSessionNonceStore : IAppSessionNonceStore
{
    private readonly SingleUseMemoryStore<bool> _store;

    /// <summary>Creates the store.</summary>
    /// <param name="clock">The clock expiry is measured on. Tests pass a fixed one.</param>
    /// <param name="capacity">The most nonces held live at once. At least 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is below 1.</exception>
    public InMemoryAppSessionNonceStore(TimeProvider clock, int capacity)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _store = new SingleUseMemoryStore<bool>(clock, capacity);
    }

    /// <inheritdoc />
    public ValueTask<bool> AddAsync(string nonce, TimeSpan lifetime, CancellationToken ct = default) =>
        ValueTask.FromResult(_store.TryAdd(nonce, true, lifetime));

    /// <inheritdoc />
    public ValueTask<bool> TakeAsync(string nonce, CancellationToken ct = default) =>
        ValueTask.FromResult(_store.TryTake(nonce, out _));
}

/// <summary>
/// The default web-ticket store: a bounded dictionary in this process's memory. The same single-process caveat as
/// <see cref="InMemoryAppSessionNonceStore"/> applies: a ticket issued by one copy cannot be redeemed at another.
/// </summary>
public sealed class InMemoryAppSessionTicketStore : IAppSessionTicketStore
{
    private readonly SingleUseMemoryStore<WebTicketGrant> _store;

    /// <summary>Creates the store.</summary>
    /// <param name="clock">The clock expiry is measured on. Tests pass a fixed one.</param>
    /// <param name="capacity">The most tickets held live at once. At least 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is below 1.</exception>
    public InMemoryAppSessionTicketStore(TimeProvider clock, int capacity)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _store = new SingleUseMemoryStore<WebTicketGrant>(clock, capacity);
    }

    /// <inheritdoc />
    public ValueTask<bool> AddAsync(string ticket, WebTicketGrant grant, TimeSpan lifetime, CancellationToken ct = default) =>
        ValueTask.FromResult(_store.TryAdd(ticket, grant, lifetime));

    /// <inheritdoc />
    public ValueTask<WebTicketGrant?> TakeAsync(string ticket, CancellationToken ct = default) =>
        ValueTask.FromResult(_store.TryTake(ticket, out var grant) ? grant : null);
}

/// <summary>
/// A small in-memory map of single-use values with a time to live. Taking a value removes it, so a nonce or a ticket works once.
/// <para>
/// Not the shared memory cache: a flood of nonces must not evict another caller's entries, and this store has its own bound. It
/// takes a <see cref="TimeProvider"/> so expiry is testable.
/// </para>
/// </summary>
internal sealed class SingleUseMemoryStore<T>
{
    private readonly ConcurrentDictionary<string, (T Value, DateTimeOffset Expires)> _items = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly int _capacity;

    public SingleUseMemoryStore(TimeProvider clock, int capacity)
    {
        _clock = clock;
        _capacity = capacity;
    }

    /// <summary>False when the store is full of live entries: the caller refuses rather than evicting someone else's.</summary>
    public bool TryAdd(string key, T value, TimeSpan ttl)
    {
        if (_items.Count >= _capacity)
            Prune();
        if (_items.Count >= _capacity)
            return false;

        return _items.TryAdd(key, (value, _clock.GetUtcNow().Add(ttl)));
    }

    /// <summary>Removes and returns the value, or false when it was never issued, was used already, or has expired.</summary>
    public bool TryTake(string key, out T value)
    {
        value = default!;
        if (!_items.TryRemove(key, out var entry))
            return false;
        if (entry.Expires <= _clock.GetUtcNow())
            return false;

        value = entry.Value;
        return true;
    }

    private void Prune()
    {
        var now = _clock.GetUtcNow();
        foreach (var kv in _items)
        {
            if (kv.Value.Expires <= now)
                _items.TryRemove(kv.Key, out _);
        }
    }
}
