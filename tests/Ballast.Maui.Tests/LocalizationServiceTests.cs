// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

using Ballast.Maui.Localization;

namespace Ballast.Maui.Tests;

/// <summary>
/// The app's language: the saved choice is restored, an unknown one falls back to the first supported language, a change is saved and raised, and a count picks its plural form.
/// The strings come from the test assembly's own resources (<c>Strings.resx</c> and <c>Strings.hr.resx</c>).
/// </summary>
public sealed class LocalizationServiceTests : IDisposable
{
    private readonly CultureInfo _culture = Thread.CurrentThread.CurrentCulture;
    private readonly CultureInfo _uiCulture = Thread.CurrentThread.CurrentUICulture;
    private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    public void Dispose()
    {
        CultureInfo.DefaultThreadCurrentCulture = _defaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
        Thread.CurrentThread.CurrentCulture = _culture;
        Thread.CurrentThread.CurrentUICulture = _uiCulture;
    }

    private static LocalizationOptions Options(Func<string, int, string, string>? plural = null) => new()
    {
        ResourceBaseName = "Ballast.Maui.Tests.Strings",
        ResourceAssembly = typeof(LocalizationServiceTests).Assembly,
        SupportedLanguages = ["en", "hr"],
        Cultures = new Dictionary<string, string>(StringComparer.Ordinal) { ["en"] = "en-US", ["hr"] = "hr-HR" },
        PluralKey = plural,
    };

    [Fact]
    public void A_saved_language_is_restored()
    {
        var preferences = new FakePreferenceStore();
        preferences.Set("app_language", "hr", null);

        var service = LocalizationService.Create(Options(), preferences);

        Assert.Equal("hr-HR", service.CurrentCulture.Name);
        Assert.Equal("Bok", service["Greeting"]);
    }

    [Fact]
    public void An_unsupported_saved_language_falls_back_to_the_first_supported_one()
    {
        var preferences = new FakePreferenceStore();
        preferences.Set("app_language", "xx", null);

        var service = LocalizationService.Create(Options(), preferences);

        Assert.Equal("en-US", service.CurrentCulture.Name);
        Assert.Equal("Hello", service["Greeting"]);
    }

    [Fact]
    public void Setting_a_culture_saves_the_choice_raises_the_change_and_shows_the_new_strings()
    {
        var preferences = new FakePreferenceStore();
        var service = LocalizationService.Create(Options(), preferences);
        var changes = 0;
        service.CultureChanged += (_, _) => changes++;

        service.SetCulture(new CultureInfo("hr-HR"));

        Assert.Equal(1, changes);
        Assert.Equal("Bok", service["Greeting"]);
        Assert.Equal("hr", preferences.Get("app_language", string.Empty, null));
    }

    [Fact]
    public void Without_preferences_the_choice_is_kept_in_memory_only()
    {
        var service = LocalizationService.Create(Options(), null);

        service.SetCulture(new CultureInfo("hr-HR"));

        Assert.Equal("Bok", service["Greeting"]);
    }

    [Fact]
    public void A_missing_key_is_shown_as_the_key()
    {
        var service = LocalizationService.Create(Options(), null);

        Assert.Equal("NoSuchKey", service["NoSuchKey"]);
        Assert.Equal("NoSuchKey", service.GetString("NoSuchKey"));
    }

    [Theory]
    [InlineData(1, "1 ticket")]
    [InlineData(5, "5 tickets")]
    public void A_count_picks_its_plural_form(int count, string expected)
    {
        var service = LocalizationService.Create(Options(), null);

        Assert.Equal(expected, service.Counted("Tickets", count, count));
    }

    [Fact]
    public void A_plural_key_rule_can_be_supplied()
    {
        var service = LocalizationService.Create(Options((baseKey, count, language) => $"{baseKey}.{language}.{count}"), null);

        Assert.Equal("Tickets.en.5", service.Counted("Tickets", 5, 5));
    }

    [Fact]
    public void Initialize_makes_the_one_instance_and_may_be_called_once()
    {
        var first = LocalizationService.Initialize(Options(), null);

        Assert.Same(first, LocalizationService.Instance);
        Assert.Throws<InvalidOperationException>(() => LocalizationService.Initialize(Options(), null));
    }

    [Fact]
    public void A_null_culture_is_refused()
    {
        var service = LocalizationService.Create(Options(), null);

        Assert.Throws<ArgumentNullException>(() => service.SetCulture(null!));
    }
}
