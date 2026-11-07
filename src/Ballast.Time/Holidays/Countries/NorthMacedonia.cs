// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// NOT CONFIRMED: the cited official gazette (slvesnik.com.mk) failed its certificate check, and the Assembly site
// (sobranie.mk) returned HTTP 403. No official text was read, so the list and the Sunday rule are unverified (law: Zakon za
// praznicite na Republika Makedonija, Sl. vesnik 51/98 and amendments). No text read contradicts the rows below. Religious
// days that follow the lunar calendar (Eid al-Fitr, Eid al-Adha) cannot be a rule and are not included. Not registered in
// PublicHolidayCalendar until confirmed.

namespace Ballast.Time;

/// <summary>North Macedonia's public holidays under the law on holidays.</summary>
internal static class MkHolidays
{
    /// <summary>
    /// North Macedonia: national holidays under the law on holidays, with the Sunday rule (a holiday on a Sunday gives the next
    /// working day off). Lunar-calendar holidays (Eid al-Fitr, Eid al-Adha) are not included.
    /// </summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "MK",
        null,
        "National public holidays under the Macedonian law on holidays, with the day off in lieu for a holiday that falls on a Sunday. " +
        "Lunar-calendar holidays (Eid al-Fitr and Eid al-Adha) are not included.",
        [
            // Rules: Fixed(month, day, local, english, substitutable); OrthodoxEaster(offset, local, english, substitutable).
            HolidayRule.Fixed(1, 1, "Нова година", "New Year's Day", true),
            HolidayRule.Fixed(1, 7, "Прв ден Божик", "Orthodox Christmas", true),
            HolidayRule.OrthodoxEaster(0, "Прв ден Велигден", "Orthodox Easter Sunday", true),
            HolidayRule.Fixed(5, 1, "Ден на трудот", "Labour Day", true),
            HolidayRule.Fixed(5, 24, "Св. Кирил и Методиј", "Saints Cyril and Methodius Day", true),
            HolidayRule.Fixed(8, 2, "Илинден", "Ilinden (Republic Day)", true),
            HolidayRule.Fixed(9, 8, "Ден на независноста", "Independence Day", true),
            HolidayRule.Fixed(10, 11, "Ден на народното востание", "Day of the People's Uprising", true),
            HolidayRule.Fixed(10, 23, "Ден на македонската револуционерна борба", "Macedonian Revolutionary Struggle Day", true),
            HolidayRule.Fixed(12, 8, "Св. Климент Охридски", "St Clement of Ohrid Day", true),
        ],
        true);
}
