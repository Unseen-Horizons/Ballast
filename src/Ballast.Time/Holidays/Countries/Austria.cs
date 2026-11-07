// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Confirmed against https://www.ris.bka.gv.at/GeltendeFassung.wxe?Abfrage=Bundesnormen&Gesetzesnummer=10008541, read 2026-11-06
// The page is the Arbeitsruhegesetz (ARG), Section 7 Abs. 2, which lists the 13 Feiertage below (consolidated version shown 06.11.2026).
// The plan named the Feiertagsruhegesetz 1957, Section 1, which is not where the list is. All 13 rows match the law's list.
// Easter-based days are named without offsets in the law; the offsets are the Western Easter offsets. No moved days.

namespace Ballast.Time;

/// <summary>
/// The Austrian public holidays (Arbeitsruhegesetz, Section 7 Abs. 2).
/// </summary>
internal static class AtHolidays
{
    /// <summary>The Austrian calendar.</summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "AT",
        null,
        "Nationwide public holidays of the Arbeitsruhegesetz, Section 7 Abs. 2. The list has no regional days.",
        [
            HolidayRule.Fixed(1, 1, "Neujahr", "New Year's Day"),
            HolidayRule.Fixed(1, 6, "Heilige Drei Könige", "Epiphany"),
            HolidayRule.WesternEaster(1, "Ostermontag", "Easter Monday"),
            HolidayRule.Fixed(5, 1, "Staatsfeiertag", "National Holiday (Labour Day)"),
            HolidayRule.WesternEaster(39, "Christi Himmelfahrt", "Ascension Day"),
            HolidayRule.WesternEaster(50, "Pfingstmontag", "Whit Monday"),
            HolidayRule.WesternEaster(60, "Fronleichnam", "Corpus Christi"),
            HolidayRule.Fixed(8, 15, "Mariä Himmelfahrt", "Assumption of Mary"),
            HolidayRule.Fixed(10, 26, "Nationalfeiertag", "Austrian National Day"),
            HolidayRule.Fixed(11, 1, "Allerheiligen", "All Saints' Day"),
            HolidayRule.Fixed(12, 8, "Mariä Empfängnis", "Immaculate Conception"),
            HolidayRule.Fixed(12, 25, "Christtag", "Christmas Day"),
            HolidayRule.Fixed(12, 26, "Stefanitag", "St Stephen's Day"),
        ],
        sundayMovesToNextWorkingDay: false);
}
