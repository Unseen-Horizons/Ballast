// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

namespace Ballast.Maui.Localization;

/// <summary>
/// The languages the app offers, where its strings are, and how it remembers the choice. Nothing here names an app: the strings, the
/// languages and the plural rule all come in from the app.
/// </summary>
public sealed record LocalizationOptions
{
    /// <summary>
    /// The base name of the resource table, for example <c>MyApp.Resources.Strings.AppResources</c>. Required.
    /// </summary>
    public required string ResourceBaseName { get; init; }

    /// <summary>The assembly that holds the resource table. Required.</summary>
    public required Assembly ResourceAssembly { get; init; }

    /// <summary>
    /// The two-letter language codes the app offers, in any order. The first is used when none of them matches the device. The default is
    /// English alone.
    /// </summary>
    public IReadOnlyList<string> SupportedLanguages { get; init; } = ["en"];

    /// <summary>
    /// The culture each language is shown in, by two-letter code, for example <c>hr</c> to <c>hr-HR</c>. A language with no entry uses a
    /// culture named after the code. The default maps <c>en</c> to <c>en-US</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Cultures { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal) { ["en"] = "en-US" };

    /// <summary>
    /// The key the chosen language is saved under. The default is <c>app_language</c>, which is what installs already use.
    /// </summary>
    public string PreferenceKey { get; init; } = "app_language";

    /// <summary>The preferences container the choice is kept in, or null for the default one.</summary>
    public string? PreferencesSharedName { get; init; }

    /// <summary>
    /// Builds the resource key for a count, given the base key, the count and the two-letter language. The default is English: the key
    /// with <c>_One</c> for a count of 1 and <c>_Other</c> for the rest. An app with other plural forms supplies its own.
    /// </summary>
    public Func<string, int, string, string>? PluralKey { get; init; }
}
