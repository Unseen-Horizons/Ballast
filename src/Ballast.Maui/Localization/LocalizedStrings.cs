// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel;

namespace Ballast.Maui.Localization;

/// <summary>
/// A bindable view over a <see cref="LocalizationService"/>'s string table, and the reason a language change reaches text that is already on screen.
/// </summary>
/// <remarks>
/// A binding made through the translate markup extension binds to <c>[SomeKey]</c> on this one shared instance. When the culture changes, the
/// property-changed event for the indexer tells every one of those bindings to re-read. Nothing is per key: the name <c>Item[]</c> is the .NET
/// convention for "every indexed value changed".
/// </remarks>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    private readonly LocalizationService _service;

    /// <summary>Creates the view over one service, and listens for its culture changes.</summary>
    /// <param name="service">The service whose strings are shown.</param>
    public LocalizedStrings(LocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _service.CultureChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>The string for <paramref name="key"/> in the current culture, or the key itself.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The string.</returns>
    public string this[string key] => _service[key];

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
}
