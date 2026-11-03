// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel;

namespace Ballast.Maui.Connectivity;

/// <summary>
/// The app's one live connectivity state: a single subscription to connectivity changes, exposed as observable state that an
/// offline banner binds to.
/// </summary>
/// <remarks>
/// <para>
/// The state holds the only subscription to the platform's connectivity events, and it lives on a singleton: pages bind to it,
/// never the other way round, so no page is kept alive by it. <see cref="Dispose"/> drops the subscription.
/// </para>
/// <para>
/// Changes arrive on a platform thread. The state records the thread that built it (the UI thread on a phone) and moves each
/// change back to that thread before it sets anything, because the banner binds on it. Built with no synchronisation context,
/// as in a test, the change is set inline.
/// </para>
/// <para>
/// The observable state is a plain <see cref="INotifyPropertyChanged"/> implementation, so the library needs no toolkit package.
/// </para>
/// </remarks>
public sealed class OfflineModeState : INotifyPropertyChanged, IDisposable
{
    private readonly IConnectivitySource _source;
    private readonly SynchronizationContext? _owner;
    private bool _disposed;
    private bool _isOffline;
    private bool _isExpanded;

    /// <summary>Creates the state and subscribes to <paramref name="source"/>.</summary>
    /// <param name="source">Where connectivity is read and changes come from.</param>
    public OfflineModeState(IConnectivitySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _owner = SynchronizationContext.Current;
        _isOffline = source.IsOfflineNow();
        source.Changed += OnChanged;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether the device is offline now. Set from the source's answer and its changes.</summary>
    public bool IsOffline
    {
        get => _isOffline;
        set => SetProperty(ref _isOffline, value, nameof(IsOffline));
    }

    /// <summary>Whether the banner shows its explanation or just the one-line title.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value, nameof(IsExpanded));
    }

    /// <summary>Stops listening to the source. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }

    private void OnChanged(object? sender, bool isOffline)
    {
        if (_owner is null)
            IsOffline = isOffline;
        else
            _owner.Post(_ => IsOffline = isOffline, null);
    }

    private void SetProperty(ref bool field, bool value, string name)
    {
        if (field == value)
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
