// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Resources;

using Ballast.Maui.Storage;

namespace Ballast.Maui.Localization;

/// <summary>
/// The app's language: which culture it shows, the strings for a key, the plural form of a counted string, and the choice the person made.
/// </summary>
/// <remarks>
/// <para>
/// The service is created once at start-up by <see cref="Initialize"/>, which also makes it reachable from XAML, where no constructor can take
/// it. Every other caller takes the instance it was given.
/// </para>
/// <para>
/// A change of language raises <see cref="CultureChanged"/>, and <see cref="Strings"/> re-raises it as an indexer change. Every binding made
/// through the translate markup extension re-reads, so text already on screen changes language with it. Strings set from code (a tab title, read
/// once in a constructor) do not: the app rebuilds those itself after a change.
/// </para>
/// </remarks>
public sealed class LocalizationService
{
    private static LocalizationService? _instance;

    private readonly LocalizationOptions _options;
    private readonly ResourceManager _resources;
    private readonly IPreferenceStore? _preferences;

    private LocalizationService(LocalizationOptions options, IPreferenceStore? preferences)
    {
        _options = options;
        _preferences = preferences;
        _resources = new ResourceManager(options.ResourceBaseName, options.ResourceAssembly);
        CurrentCulture = CultureInfo.CurrentUICulture;
        Strings = new LocalizedStrings(this);
    }

    /// <summary>The service made by <see cref="Initialize"/>. Throws until it has been called.</summary>
    /// <exception cref="InvalidOperationException">No service has been initialised yet.</exception>
    public static LocalizationService Instance =>
        _instance ?? throw new InvalidOperationException("LocalizationService.Initialize has not been called.");

    /// <summary>The culture the strings are shown in now.</summary>
    public CultureInfo CurrentCulture { get; private set; }

    /// <summary>The indexer-backed string table that XAML bindings listen to.</summary>
    public LocalizedStrings Strings { get; }

    /// <summary>
    /// Raised after <see cref="SetCulture"/>, so that text already on screen changes language with it rather than only on the next navigation.
    /// </summary>
    public event EventHandler? CultureChanged;

    /// <summary>
    /// The string for <paramref name="key"/> in the current culture, or the key itself when there is no such string or the table cannot be read.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The string, or the key.</returns>
    public string this[string key]
    {
        get
        {
            try
            {
                return _resources.GetString(key, CurrentCulture) ?? key;
            }
            catch (Exception ex) when (ex is MissingManifestResourceException or InvalidOperationException)
            {
                return key;
            }
        }
    }

    /// <summary>The string for <paramref name="key"/>, the same as the indexer.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The string, or the key.</returns>
    public string GetString(string key) => this[key];

    /// <summary>
    /// A string that carries a count, in the plural form the count takes in the current language. The count is normally one of the
    /// arguments, so "1 ticket" and "5 tickets" are separate resources.
    /// </summary>
    /// <param name="baseKey">The key without its plural suffix.</param>
    /// <param name="count">The count that picks the form.</param>
    /// <param name="args">The format arguments.</param>
    /// <returns>The formatted string.</returns>
    public string Counted(string baseKey, int count, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseKey);
        var language = CurrentCulture.TwoLetterISOLanguageName;
        var key = _options.PluralKey is { } plural ? plural(baseKey, count, language) : DefaultPluralKey(baseKey, count);
        return string.Format(CurrentCulture, this[key], args);
    }

    /// <summary>
    /// Changes the culture, makes it the default for every thread and every continuation, saves the choice, and raises
    /// <see cref="CultureChanged"/>.
    /// </summary>
    /// <param name="culture">The culture to show.</param>
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CurrentCulture = culture;

        // Setting only the calling thread leaves every other thread, and every continuation that resumes on the thread pool, on the old
        // culture, so a language change appears to half-apply.
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        _preferences?.Set(_options.PreferenceKey, culture.TwoLetterISOLanguageName, _options.PreferencesSharedName);
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows the saved language, or the device's when none is saved, or the first supported language when the device's is not one the app
    /// offers. Called once at start-up.
    /// </summary>
    public void RestoreSavedCulture()
    {
        var saved = _preferences?.Get(_options.PreferenceKey, string.Empty, _options.PreferencesSharedName) ?? string.Empty;
        var language = string.IsNullOrEmpty(saved) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : saved;
        if (!_options.SupportedLanguages.Contains(language, StringComparer.Ordinal))
            language = _options.SupportedLanguages[0];

        var name = _options.Cultures.TryGetValue(language, out var mapped) ? mapped : language;
        SetCulture(new CultureInfo(name));
    }

    /// <summary>
    /// Creates the service, makes it the one <see cref="Instance"/>, and shows the saved language.
    /// </summary>
    /// <param name="options">The languages and the resource table.</param>
    /// <param name="preferences">Where the choice is saved. Null keeps the choice in memory only.</param>
    /// <returns>The service.</returns>
    /// <exception cref="InvalidOperationException">It has already been initialised.</exception>
    public static LocalizationService Initialize(LocalizationOptions options, IPreferenceStore? preferences)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_instance is not null)
            throw new InvalidOperationException("LocalizationService.Initialize may be called once.");

        var service = new LocalizationService(options, preferences);
        _instance = service;
        service.RestoreSavedCulture();
        return service;
    }

    /// <summary>Creates a service that is not the process-wide one. For tests, and for an app that keeps its own instance.</summary>
    /// <param name="options">The languages and the resource table.</param>
    /// <param name="preferences">Where the choice is saved, or null.</param>
    /// <returns>The service, with the saved language shown.</returns>
    public static LocalizationService Create(LocalizationOptions options, IPreferenceStore? preferences)
    {
        ArgumentNullException.ThrowIfNull(options);
        var service = new LocalizationService(options, preferences);
        service.RestoreSavedCulture();
        return service;
    }

    private static string DefaultPluralKey(string baseKey, int count) => count == 1 ? baseKey + "_One" : baseKey + "_Other";
}
