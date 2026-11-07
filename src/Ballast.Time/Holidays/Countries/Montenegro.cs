// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// NOT CONFIRMED: the cited government page (gov.me) did not open (connection refused), and the official gazette file
// (sluzbenilist.me, document 387932) gave no holiday text. The text read from paragraf.me (an unofficial legal database,
// read 2026-11-06) is the Law on State and Other Holidays (Sl. list RCG 27/2007, Sl. list CG 36/2013). That page says it was
// current only to 31 March 2021; the 2021 and 2023 amendments were not read. Rows below follow that text. It contradicts the
// plan: Orthodox Christmas and Orthodox Easter are not days off in that text, so they are not listed here.
// The Sunday rule of that text (two following days move when a holiday falls on a Sunday) cannot be expressed by the engine,
// so the calendar does not move Sunday holidays. Not registered in PublicHolidayCalendar until confirmed.

namespace Ballast.Time;

/// <summary>Montenegro's public holidays under the law on state and other holidays.</summary>
internal static class MeHolidays
{
    /// <summary>
    /// Montenegro: each holiday is two days (the holiday and the next day). Lists those days under the law on state and other
    /// holidays. Orthodox Christmas and Orthodox Easter are not listed. The law's rule for a holiday that falls on a Sunday
    /// (the following two days move) is not modelled here, so a Sunday holiday gets no day off in lieu.
    /// </summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "ME",
        null,
        "National public holidays under the Montenegrin law on state and other holidays, both days of each two-day holiday. " +
        "Orthodox Christmas and Orthodox Easter are not included. The law's Sunday rule (the following two days move) is not modelled.",
        [
            // Rules: Fixed(month, day, local, english, substitutable). Substitutable is false: this calendar does not move days.
            HolidayRule.Fixed(1, 1, "Nova godina", "New Year's Day", false),
            HolidayRule.Fixed(1, 2, "Nova godina", "New Year's Day (second day)", false),
            HolidayRule.Fixed(5, 1, "Praznik rada", "Labour Day", false),
            HolidayRule.Fixed(5, 2, "Praznik rada", "Labour Day (second day)", false),
            HolidayRule.Fixed(5, 21, "Dan nezavisnosti", "Independence Day", false),
            HolidayRule.Fixed(5, 22, "Dan nezavisnosti", "Independence Day (second day)", false),
            HolidayRule.Fixed(7, 13, "Dan državnosti", "Statehood Day", false),
            HolidayRule.Fixed(7, 14, "Dan državnosti", "Statehood Day (second day)", false),
        ],
        false);
}
