// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

using Ballast.Maui.Helpers;
using Ballast.Maui.Localization;

using Microsoft.Maui.Controls.Xaml;

namespace Ballast.Maui.Views;

/// <summary>
/// Resolves a resource key in XAML. A markup extension's value used to be produced once, at parse time, so a page kept whatever language it was
/// built in. This one hands back a binding against <see cref="LocalizedStrings"/>, which re-reads when the culture changes. Where a binding cannot go
/// (a <c>Setter.Value</c>, whose target is not a bindable property), it returns the string.
/// </summary>
[ContentProperty(nameof(Key))]
[RequireService([typeof(IProvideValueTarget)])]
public sealed class TranslateExtension : IMarkupExtension
{
    /// <summary>The resource key to show.</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public object ProvideValue(IServiceProvider serviceProvider)
    {
        var target = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (target?.TargetObject is BindableObject && target.TargetProperty is BindableProperty)
            return new Microsoft.Maui.Controls.Binding($"[{Key}]", Microsoft.Maui.Controls.BindingMode.OneWay, source: LocalizationService.Instance.Strings);

        return LocalizationService.Instance[Key];
    }
}

/// <summary>
/// A part of a page that is built the first time it is wanted, not when the page is. The page's XAML holds an empty <see cref="ContentView"/>
/// (the host). <see cref="Ensure"/> creates the view and puts it in the host the first time it is called with <c>true</c>, and every later call
/// leaves it alone, so a section that is hidden and shown again is not rebuilt. Creating a view inside XAML costs its whole tree whether or not it
/// is visible, so a page with mutually exclusive sections should build only the one it shows.
/// </summary>
/// <typeparam name="TView">The section's view.</typeparam>
public sealed class LazySection<TView> where TView : Microsoft.Maui.Controls.View
{
    private readonly Microsoft.Maui.Controls.ContentView _host;
    private readonly Func<TView> _create;
    private readonly FirstUse _gate = new();

    /// <summary>Creates the section.</summary>
    /// <param name="host">The empty view the section goes into.</param>
    /// <param name="create">Builds the section's view.</param>
    public LazySection(Microsoft.Maui.Controls.ContentView host, Func<TView> create)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(create);
        _host = host;
        _create = create;
    }

    /// <summary>The section's view, or null until it has first been wanted.</summary>
    public TView? View { get; private set; }

    /// <summary>Builds the section when <paramref name="wanted"/> and it has not been built yet. Call it on the UI thread.</summary>
    /// <param name="wanted">Whether the section is shown now.</param>
    public void Ensure(bool wanted)
    {
        if (!_gate.Take(wanted))
            return;

        View = _create();
        _host.Content = View;
    }
}

/// <summary>
/// Reads a colour out of the app's resource dictionaries by key, for controls that set colours in code. XAML pages use the theme-binding markup with
/// a pair of resources; code cannot, so it names the same resources here.
/// </summary>
public static class ThemeColors
{
    /// <summary>The colour registered under <paramref name="key"/>, or <paramref name="missing"/> (with a trace line) when there is none.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="missing">
    /// The colour to answer when the key is not there. The default is magenta, which is not a design token, so a mistyped key shows on screen in a debug build.
    /// </param>
    /// <returns>The colour.</returns>
    public static Microsoft.Maui.Graphics.Color Get(string key, Microsoft.Maui.Graphics.Color? missing = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        missing ??= Microsoft.Maui.Graphics.Colors.Magenta;

        if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Microsoft.Maui.Graphics.Color color)
            return color;

        Trace.WriteLine($"[ThemeColors] No colour resource named '{key}'.");
        return missing;
    }
}

