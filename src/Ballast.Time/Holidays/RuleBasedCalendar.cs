// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time;

/// <summary>
/// A public holiday calendar computed from a table of rules, for one country or one region of a country.
/// </summary>
/// <remarks>
/// <para>
/// Every date is computed from the rules for the year asked for; nothing is fetched and nothing is cached. The range of
/// years is 2020 to 2100 for every calendar, because a holiday law changes over time and a rule is only known to hold for
/// the years it was checked against.
/// </para>
/// <para>
/// When <c>sundayMovesToNextWorkingDay</c> is set, a substitutable holiday that falls on a Sunday gets a day in lieu on
/// the first following day that is not a Saturday, a Sunday, or another listed date. The day in lieu is listed with
/// <see cref="PublicHoliday.IsSubstituteDay"/> set. A Saturday holiday is not moved.
/// </para>
/// </remarks>
internal sealed class RuleBasedCalendar : IPublicHolidayCalendar
{
    /// <summary>The first year every rule-based calendar covers.</summary>
    internal const int SupportedFirstYear = 2020;

    /// <summary>The last year every rule-based calendar covers.</summary>
    internal const int SupportedLastYear = 2100;

    private const string DayInLieuSuffix = " (day off in lieu)";

    private readonly IReadOnlyList<HolidayRule> _rules;
    private readonly bool _sundayMovesToNextWorkingDay;

    /// <summary>
    /// Creates a calendar from its rules.
    /// </summary>
    /// <param name="iso2">The ISO 3166-1 alpha-2 code, in upper case.</param>
    /// <param name="region">The region code, or <see langword="null"/> for the country's own calendar.</param>
    /// <param name="scope">The one-sentence statement of what the list holds and leaves out.</param>
    /// <param name="rules">The rules. Their order is kept only where two holidays fall on the same date.</param>
    /// <param name="sundayMovesToNextWorkingDay">True when a substitutable Sunday holiday gets a day in lieu.</param>
    /// <exception cref="ArgumentException">The code is not two upper-case ASCII letters, or the region or scope is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public RuleBasedCalendar(
        string iso2,
        string? region,
        string scope,
        IReadOnlyList<HolidayRule> rules,
        bool sundayMovesToNextWorkingDay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iso2);
        if (iso2.Length != 2 || !char.IsAsciiLetterUpper(iso2[0]) || !char.IsAsciiLetterUpper(iso2[1]))
        {
            throw new ArgumentException("A country code is two upper-case ASCII letters.", nameof(iso2));
        }

        if (region is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(region);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(rules);

        CountryIso2 = iso2;
        Region = region;
        Scope = scope;
        _rules = [.. rules];
        _sundayMovesToNextWorkingDay = sundayMovesToNextWorkingDay;
    }

    /// <inheritdoc/>
    public string CountryIso2 { get; }

    /// <inheritdoc/>
    public string? Region { get; }

    /// <inheritdoc/>
    public string Scope { get; }

    /// <inheritdoc/>
    public int FirstYear => SupportedFirstYear;

    /// <inheritdoc/>
    public int LastYear => SupportedLastYear;

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">The year of <paramref name="day"/> is outside 2020 to 2100.</exception>
    public bool IsHoliday(DateOnly day)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day.Year, SupportedFirstYear, nameof(day));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day.Year, SupportedLastYear, nameof(day));

        return InYear(day.Year).Any(holiday => holiday.Date == day);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside 2020 to 2100.</exception>
    /// <exception cref="InvalidOperationException">A day in lieu would fall outside the year, which no rule table does.</exception>
    public IReadOnlyList<PublicHoliday> InYear(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, SupportedFirstYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, SupportedLastYear);

        List<PublicHoliday> holidays = [];
        List<(HolidayRule Rule, DateOnly Date)> dated = [];
        foreach (HolidayRule rule in _rules)
        {
            DateOnly date = rule.DateIn(year);
            dated.Add((rule, date));
            holidays.Add(new PublicHoliday(date, rule.LocalName, rule.EnglishName));
        }

        if (_sundayMovesToNextWorkingDay)
        {
            HashSet<DateOnly> taken = [.. dated.Select(entry => entry.Date)];
            IEnumerable<(HolidayRule Rule, DateOnly Date)> sundays = dated
                .Where(entry => entry.Rule.Substitutable && entry.Date.DayOfWeek == DayOfWeek.Sunday)
                .OrderBy(entry => entry.Date);

            foreach ((HolidayRule rule, DateOnly sunday) in sundays)
            {
                DateOnly substitute = sunday.AddDays(1);
                while (substitute.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || taken.Contains(substitute))
                {
                    substitute = substitute.AddDays(1);
                }

                if (substitute.Year != year)
                {
                    throw new InvalidOperationException(
                        $"The day in lieu of {rule.EnglishName} would fall outside {year}.");
                }

                taken.Add(substitute);
                holidays.Add(new PublicHoliday(
                    substitute,
                    rule.LocalName,
                    rule.EnglishName + DayInLieuSuffix,
                    IsSubstituteDay: true));
            }
        }

        return holidays.OrderBy(holiday => holiday.Date).ToList();
    }
}
