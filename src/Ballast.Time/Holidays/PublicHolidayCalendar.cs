// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time;

/// <summary>
/// The registry of public holiday calendars: the countries, and the regions of a country, that this library has a
/// calendar for.
/// </summary>
/// <remarks>
/// <para>
/// A country or region that is not registered gives <see langword="null"/>. That includes a country with no calendar at
/// all, a country whose law has not been checked against its official text, and a region the country's calendar does not
/// list. The library never guesses a calendar: a caller that gets <see langword="null"/> must refuse or ask, not fall back
/// to another country's list.
/// </para>
/// <para>
/// A country code is matched without regard to case or surrounding white space. Nothing here reads the clock or the
/// machine's zone.
/// </para>
/// </remarks>
public static class PublicHolidayCalendar
{
    // The calendars whose law text has been read and compared with its rows (see each country file's header). A calendar
    // whose header says NOT CONFIRMED is not listed here, so no caller can reach it.
    private static readonly RuleBasedCalendar[] Registered =
    [
        AtHolidays.Calendar,
        CzHolidays.Calendar,
        HrHolidays.Calendar,
        HuHolidays.Calendar,
        ItHolidays.Calendar,
        RsHolidays.Calendar,
        SiHolidays.Calendar,
    ];

    private static readonly Dictionary<(string Country, string? Region), RuleBasedCalendar> Calendars =
        Registered.ToDictionary(calendar => (calendar.CountryIso2, calendar.Region));

    private static readonly IReadOnlyList<string> Countries =
        Registered.Select(calendar => calendar.CountryIso2).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The ISO 3166-1 alpha-2 codes, in upper case and sorted, of every country that has at least one registered calendar.
    /// A country listed here may still give <see langword="null"/> for <see cref="For(string?)"/>, when only one of its
    /// regions is registered.
    /// </summary>
    public static IReadOnlyList<string> SupportedCountries => Countries;

    /// <summary>
    /// The national calendar of a country: the list its own law gives for the whole country.
    /// </summary>
    /// <param name="countryIso2">
    /// The ISO 3166-1 alpha-2 code, for example <c>HR</c>. Case and surrounding white space are ignored.
    /// </param>
    /// <returns>
    /// The calendar, or <see langword="null"/> when the country is not registered, when its national list is not registered
    /// (only a region of it is), or when <paramref name="countryIso2"/> is null, blank or not a code.
    /// </returns>
    public static IPublicHolidayCalendar? For(string? countryIso2) => For(countryIso2, null);

    /// <summary>
    /// The calendar of a country or of one of its regions.
    /// </summary>
    /// <param name="countryIso2">
    /// The ISO 3166-1 alpha-2 code, for example <c>BA</c>. Case and surrounding white space are ignored.
    /// </param>
    /// <param name="region">
    /// The region code the calendar is for (a region the country's calendar lists), or <see langword="null"/>
    /// for the country's national calendar, which is the same as <see cref="For(string?)"/>. Case and surrounding white space
    /// are ignored. A blank region is not the national calendar: it gives <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The calendar, or <see langword="null"/> when no registered calendar matches the country and region.
    /// </returns>
    public static IPublicHolidayCalendar? For(string? countryIso2, string? region)
    {
        string? country = Normalize(countryIso2);
        if (country is null)
        {
            return null;
        }

        string? regionCode = null;
        if (region is not null)
        {
            regionCode = Normalize(region);
            if (regionCode is null)
            {
                return null;
            }
        }

        return Calendars.TryGetValue((country, regionCode), out RuleBasedCalendar? calendar) ? calendar : null;
    }

    private static string? Normalize(string? code)
    {
        string? trimmed = code?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }
}
