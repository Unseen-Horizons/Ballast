// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Confirmed against https://narodne-novine.nn.hr/clanci/sluzbeni/2019_11_110_2212.html, read 2026-11-06
// (Zakon o blagdanima, spomendanima i neradnim danima u Republici Hrvatskoj, NN 110/2019, Article 1; in force 1 January 2020).
// The plan's URL (2019_10_110_2207) was wrong; this is the official text. Article 1 lists 14 days, all matching the rows below.
// Amended by NN 72/2025 (https://narodne-novine.nn.hr/clanci/sluzbeni/2025_04_72_936.html), which adds 3 May to the memorial days
// (its Article 2 list, not a day off) and changes no day off, so no row changed.
// Movable days are named in the law without a rule; the offsets are the Western Easter offsets. No moved days.

namespace Ballast.Time;

/// <summary>
/// The Croatian public holidays and non-working days (NN 110/2019, Article 1).
/// </summary>
internal static class HrHolidays
{
    /// <summary>The Croatian calendar.</summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "HR",
        null,
        "Public holidays and non-working days of the Croatian holidays law (NN 110/2019, Article 1). Memorial days (spomendani) are not included.",
        [
            HolidayRule.Fixed(1, 1, "Nova godina", "New Year's Day"),
            HolidayRule.Fixed(1, 6, "Sveta tri kralja", "Epiphany"),
            HolidayRule.WesternEaster(0, "Uskrs", "Easter Sunday"),
            HolidayRule.WesternEaster(1, "Uskrsni ponedjeljak", "Easter Monday"),
            HolidayRule.Fixed(5, 1, "Praznik rada", "Labour Day"),
            HolidayRule.Fixed(5, 30, "Dan državnosti", "Statehood Day"),
            HolidayRule.WesternEaster(60, "Tijelovo", "Corpus Christi"),
            HolidayRule.Fixed(6, 22, "Dan antifašističke borbe", "Anti-Fascist Struggle Day"),
            HolidayRule.Fixed(8, 5, "Dan pobjede i domovinske zahvalnosti i Dan hrvatskih branitelja", "Victory and Homeland Thanksgiving Day"),
            HolidayRule.Fixed(8, 15, "Velika Gospa", "Assumption of Mary"),
            HolidayRule.Fixed(11, 1, "Svi sveti", "All Saints' Day"),
            HolidayRule.Fixed(11, 18, "Dan sjećanja na žrtve Domovinskog rata i Dan sjećanja na žrtvu Vukovara i Škabrnje", "Remembrance Day for the Victims of the Homeland War and of Vukovar and Škabrnja"),
            HolidayRule.Fixed(12, 25, "Božić", "Christmas Day"),
            HolidayRule.Fixed(12, 26, "Sveti Stjepan", "St Stephen's Day"),
        ],
        sundayMovesToNextWorkingDay: false);
}
