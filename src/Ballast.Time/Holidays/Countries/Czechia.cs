// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Confirmed against https://www.zakonyprolidi.cz/cs/2000-245, read 2026-11-06.
// Sections 1 and 2 of Act No. 245/2000 Coll. give the state holidays and the other holidays; section 3 makes all of them
// days of rest. Good Friday and Easter Monday are named there without a date, and their dates follow Easter.

namespace Ballast.Time;

/// <summary>
/// The state holidays and other holidays of the Czech Republic under Act No. 245/2000 Coll.
/// </summary>
internal static class CzHolidays
{
    /// <summary>The Czech national calendar (Region is null).</summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "CZ",
        region: null,
        scope: "State holidays and other holidays under Act No. 245/2000 Coll., all of them days of rest.",
        rules:
        [
            HolidayRule.Fixed(1, 1, "Nový rok, Den obnovy samostatného českého státu", "New Year's Day, Restoration Day of the Czech Independent State"),
            HolidayRule.WesternEaster(-2, "Velký pátek", "Good Friday"),
            HolidayRule.WesternEaster(1, "Velikonoční pondělí", "Easter Monday"),
            HolidayRule.Fixed(5, 1, "Svátek práce", "Labour Day"),
            HolidayRule.Fixed(5, 8, "Den vítězství", "Liberation Day"),
            HolidayRule.Fixed(7, 5, "Den slovanských věrozvěstů Cyrila a Metoděje", "Saints Cyril and Methodius Day"),
            HolidayRule.Fixed(7, 6, "Den upálení mistra Jana Husa", "Jan Hus Day"),
            HolidayRule.Fixed(9, 28, "Den české státnosti", "Czech Statehood Day"),
            HolidayRule.Fixed(10, 28, "Den vzniku samostatného československého státu", "Independent Czechoslovak State Day"),
            HolidayRule.Fixed(11, 17, "Den boje za svobodu a demokracii a Mezinárodní den studentstva", "Struggle for Freedom and Democracy and International Students' Day"),
            HolidayRule.Fixed(12, 24, "Štědrý den", "Christmas Eve"),
            HolidayRule.Fixed(12, 25, "1. svátek vánoční", "Christmas Day"),
            HolidayRule.Fixed(12, 26, "2. svátek vánoční", "Boxing Day"),
        ],
        sundayMovesToNextWorkingDay: false);
}
