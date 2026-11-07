// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// NOT CONFIRMED: the law text could not be read. The official PDF of Law No. 7651 "Për festat zyrtare dhe ditët përkujtimore"
// (http://www.kqk.gov.al/sites/default/files/publikime/ligj_7651_-_per_festat_zyrtare_e_ditet_perkujtimore.pdf) returned
// HTTP 404. The rows follow the English Wikipedia article "Public holidays in Albania" (read 2026-11-06, which quotes a table
// of 15 holidays; 14 rows were shown) and a Mondaq note on Law 1/2018 (https://www.mondaq.com/Article/712356). Corrections
// from the plan: Mother Teresa Day moved from 19 October to 5 September (Law 1/2018, per both secondary sources); 22 November
// (Alphabet Day) added. Eid al-Fitr and Eid al-Adha follow the lunar calendar and are not included. The weekend rule of the
// article (a holiday on a weekend gives the first working day after it off) includes Saturdays, which the engine cannot
// express; Saturday holidays are not moved here.

namespace Ballast.Time;

/// <summary>
/// Albania: the national public holidays under Law No. 7651 of 1992 as amended by Law No. 1/2018 (not confirmed; see the
/// file header).
/// </summary>
internal static class AlHolidays
{
    /// <summary>
    /// The national public holidays. Eid al-Fitr and Eid al-Adha are lunar and not included.
    /// </summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "AL",
        null,
        "National public holidays under Law No. 7651 of 1992 as amended by Law No. 1/2018. Eid al-Fitr and Eid al-Adha " +
        "(lunar calendar) are not included. A holiday that falls on a Sunday gives the next day off; Saturday holidays are not moved.",
        [
            HolidayRule.Fixed(1, 1, "Viti i Ri", "New Year's Day", true),
            HolidayRule.Fixed(1, 2, "Viti i Ri", "New Year's Day (second day)", true),
            HolidayRule.Fixed(3, 14, "Dita e Verës", "Summer Day", true),
            HolidayRule.Fixed(3, 22, "Dita e Nevruzit", "Nevruz Day", true),
            HolidayRule.WesternEaster(0, "Pashka Katolike", "Catholic Easter Sunday"),
            HolidayRule.OrthodoxEaster(0, "Pashka Ortodokse", "Orthodox Easter Sunday"),
            HolidayRule.Fixed(5, 1, "Dita Ndërkombëtare e Punës", "International Workers' Day", true),
            HolidayRule.Fixed(9, 5, "Dita e Nënë Terezës", "Mother Teresa Day", true),
            HolidayRule.Fixed(11, 22, "Dita e Alfabetit", "Alphabet Day", true),
            HolidayRule.Fixed(11, 28, "Dita e Pavarësisë", "Independence Day", true),
            HolidayRule.Fixed(11, 29, "Dita e Çlirimit", "Liberation Day", true),
            HolidayRule.Fixed(12, 8, "Dita Kombëtare e Rinisë", "National Youth Day", true),
            HolidayRule.Fixed(12, 25, "Krishtlindjet", "Christmas Day", true),
        ],
        true);
}
