// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Confirmed against https://www.normattiva.it/uri-res/N2Ls?urn:nir:stato:legge:1949-05-27;260~art2, read 2026-11-06.
// Article 1 of Law 260/1949 (as amended by Law 336/2000) names 2 June; Article 2 names the other days below.
// 4 October (Law 151/2025, normattiva 25G00153) is NOT in this calendar: the text read says it is a festa nazionale
// but does not state that work stops, and the Article 2 page shows it in double parentheses.

namespace Ballast.Time;

/// <summary>
/// The national public holidays of Italy under Law 260/1949 and its amendments.
/// </summary>
internal static class ItHolidays
{
    /// <summary>The Italian national calendar (Region is null).</summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "IT",
        region: null,
        scope: "National public holidays under Law 260/1949 and its amendments; local patron-saint days and 4 October (Law 151/2025) are not included.",
        rules:
        [
            HolidayRule.Fixed(1, 1, "Capodanno", "New Year's Day"),
            HolidayRule.Fixed(1, 6, "Epifania", "Epiphany"),
            HolidayRule.WesternEaster(0, "Pasqua", "Easter Sunday"),
            HolidayRule.WesternEaster(1, "Lunedì dell'Angelo", "Easter Monday"),
            HolidayRule.Fixed(4, 25, "Festa della Liberazione", "Liberation Day"),
            HolidayRule.Fixed(5, 1, "Festa del Lavoro", "Labour Day"),
            HolidayRule.Fixed(6, 2, "Festa della Repubblica", "Republic Day"),
            HolidayRule.Fixed(8, 15, "Assunzione di Maria (Ferragosto)", "Assumption of Mary"),
            HolidayRule.Fixed(11, 1, "Ognissanti", "All Saints' Day"),
            HolidayRule.Fixed(12, 8, "Immacolata Concezione", "Immaculate Conception"),
            HolidayRule.Fixed(12, 25, "Natale", "Christmas Day"),
            HolidayRule.Fixed(12, 26, "Santo Stefano", "St Stephen's Day"),
        ],
        sundayMovesToNextWorkingDay: false);
}
