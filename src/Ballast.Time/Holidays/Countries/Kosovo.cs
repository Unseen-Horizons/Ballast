// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// NOT CONFIRMED: the holiday list and the dates match Article 2 of Law No. 03/L-064 "Për festat zyrtare në Republikën e
// Kosovës" as printed in Gazeta Zyrtare e Republikës së Kosovës, Nr. 30, 15 June 2008, read 2026-11-06 from
// https://www.uni-gjilan.net/wp-content/uploads/2021/01/LIGJI-NR.-03-L-064-PËR-FESTAT-ZYRTARE-NË-REPUBLIKËN-E-KOSOVËS.pdf
// (a copy of the gazette text; the gazette site gzk.rks-gov.net was not reachable). No amendment to the 2008 law was found.
// Corrections from the plan: Easter Sunday becomes Easter Monday for both churches (Article 2(j) and (k): "E hëna e
// Pashkëve"); "Dita e Punës" becomes "Dita Ndërkombëtare e Punës" (Article 2(d)). Article 4 moves a holiday that falls on a
// Saturday or a Sunday to the next working day. The engine moves only Sundays, so a Saturday holiday (9 May 2026) would be
// answered wrongly: the calendar is not registered until the engine moves Saturdays too. Eid al-Fitr and Eid al-Adha
// (Article 2(f) and (g)) are lunar and are not included. The memorial days of Article 5(5) are not holidays and are not included.

namespace Ballast.Time;

/// <summary>
/// Kosovo: the official holidays of Law No. 03/L-064 (not confirmed; see the file header).
/// </summary>
internal static class XkHolidays
{
    /// <summary>
    /// The official holidays of Article 2 of the law, without the lunar Eid days.
    /// </summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "XK",
        null,
        "Official holidays of Law No. 03/L-064 on Official Holidays, Article 2, except the two Eid days (lunar). A holiday that " +
        "falls on a Saturday or a Sunday moves to the next working day under Article 4; the calendar moves Sundays only.",
        [
            HolidayRule.Fixed(1, 1, "Viti i Ri", "New Year's Day", true),
            HolidayRule.Fixed(1, 2, "Viti i Ri", "New Year's Day (second day)", true),
            HolidayRule.Fixed(1, 7, "Krishtlindjet Ortodokse", "Orthodox Christmas", true),
            HolidayRule.Fixed(2, 17, "Dita e Pavarësisë", "Independence Day", true),
            HolidayRule.Fixed(4, 9, "Dita e Kushtetutës", "Constitution Day", true),
            HolidayRule.WesternEaster(1, "Pashkët Katolike", "Catholic Easter Monday"),
            HolidayRule.OrthodoxEaster(1, "Pashkët Ortodokse", "Orthodox Easter Monday"),
            HolidayRule.Fixed(5, 1, "Dita Ndërkombëtare e Punës", "International Labour Day", true),
            HolidayRule.Fixed(5, 9, "Dita e Evropës", "Europe Day", true),
            HolidayRule.Fixed(12, 25, "Krishtlindjet Katolike", "Catholic Christmas", true),
        ],
        true);
}
