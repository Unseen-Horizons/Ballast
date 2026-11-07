// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Confirmed against https://net.jogtar.hu/jogszabaly?docid=A1200001.TV (Act I of 2012, Munka törvénykönyve, section 102 (1)),
// the consolidated text read 2026-11-06. Quoted verbatim:
//   "Munkaszüneti nap: január 1., március 15., nagypéntek, húsvéthétfő, május 1., pünkösdhétfő,
//    augusztus 20., október 23., november 1. és december 25–26."
// Húsvétvasárnap and pünkösdvasárnap are not named, so they are not in this calendar. 24 December (szenteste) is not
// named at any year, so it is not in this calendar either. No day in the list applies only from a given year, so no
// FirstYear is used.
// The page shows the text in force from 2026.X.1. Section 102 (4) names húsvétvasárnap and pünkösdvasárnap only for the
// scheduling rules of a rest day, not as munkaszüneti napok. Bridge days swapped by decree each year are not in the section
// and are not modelled.

namespace Ballast.Time;

/// <summary>
/// The public holidays (munkaszüneti napok) of Hungary under section 102 (1) of Act I of 2012.
/// </summary>
internal static class HuHolidays
{
    /// <summary>The Hungarian national calendar (Region is null).</summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "HU",
        region: null,
        scope: "Public holidays under section 102 (1) of Act I of 2012; bridge days swapped by ministerial decree each year are not included.",
        rules:
        [
            HolidayRule.Fixed(1, 1, "Újév", "New Year's Day"),
            HolidayRule.Fixed(3, 15, "Nemzeti ünnep (1848-as forradalom)", "National Day (1848 Revolution)"),
            HolidayRule.WesternEaster(-2, "Nagypéntek", "Good Friday"),
            HolidayRule.WesternEaster(1, "Húsvéthétfő", "Easter Monday"),
            HolidayRule.Fixed(5, 1, "A munka ünnepe", "Labour Day"),
            HolidayRule.WesternEaster(50, "Pünkösdhétfő", "Whit Monday"),
            HolidayRule.Fixed(8, 20, "Az államalapítás ünnepe", "Saint Stephen's Day (State Foundation)"),
            HolidayRule.Fixed(10, 23, "Nemzeti ünnep (1956-os forradalom)", "National Day (1956 Revolution)"),
            HolidayRule.Fixed(11, 1, "Mindenszentek", "All Saints' Day"),
            HolidayRule.Fixed(12, 25, "Karácsony", "Christmas Day"),
            HolidayRule.Fixed(12, 26, "Karácsony másnapja", "Boxing Day"),
        ],
        sundayMovesToNextWorkingDay: false);
}
