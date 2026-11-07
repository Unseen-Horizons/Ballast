// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Confirmed against https://www.gov.si/teme/drzavni-prazniki-in-dela-prosti-dnevi/, read 2026-11-06
// The Government's page lists the state holidays and the other days off: New Year (1 and 2 January), Prešeren Day,
// Day of Uprising, Labour Day (1 and 2 May), Statehood Day, Easter Sunday and Monday, Pentecost Sunday (binkoštna nedelja),
// Assumption, Reformation Day, All Saints, Christmas and Independence and Unity Day. Whit Monday is not listed.
// Commemorations that are not days off (8 June, 17 August, 15 September, 23 September, 25 October, 10 November, 23 November)
// are not included. No moved days.

namespace Ballast.Time;

/// <summary>
/// The Slovenian public holidays and days off (ZPDPD, Articles 1 and 2).
/// </summary>
internal static class SiHolidays
{
    /// <summary>The Slovenian calendar.</summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "SI",
        null,
        "Public holidays and days off of the Slovenian holidays law (ZPDPD). Commemorations that are not days off are not included.",
        [
            HolidayRule.Fixed(1, 1, "novo leto", "New Year's Day"),
            HolidayRule.Fixed(1, 2, "novo leto", "New Year's Day (second day)"),
            HolidayRule.Fixed(2, 8, "Prešernov dan, slovenski kulturni praznik", "Prešeren Day, Slovenian Cultural Holiday"),
            HolidayRule.WesternEaster(0, "velikonočna nedelja", "Easter Sunday"),
            HolidayRule.WesternEaster(1, "velikonočni ponedeljek", "Easter Monday"),
            HolidayRule.Fixed(4, 27, "dan upora proti okupatorju", "Day of Uprising Against the Occupation"),
            HolidayRule.Fixed(5, 1, "praznik dela", "Labour Day"),
            HolidayRule.Fixed(5, 2, "praznik dela", "Labour Day (second day)"),
            HolidayRule.WesternEaster(49, "binkoštna nedelja", "Pentecost Sunday"),
            HolidayRule.Fixed(6, 25, "dan državnosti", "Statehood Day"),
            HolidayRule.Fixed(8, 15, "Marijino vnebovzetje", "Assumption of Mary"),
            HolidayRule.Fixed(10, 31, "dan reformacije", "Reformation Day"),
            HolidayRule.Fixed(11, 1, "dan spomina na mrtve", "All Saints' Day"),
            HolidayRule.Fixed(12, 25, "božič", "Christmas Day"),
            HolidayRule.Fixed(12, 26, "dan samostojnosti in enotnosti", "Independence and Unity Day"),
        ],
        sundayMovesToNextWorkingDay: false);
}
