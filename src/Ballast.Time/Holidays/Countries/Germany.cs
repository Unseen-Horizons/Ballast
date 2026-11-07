// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// NOT CONFIRMED: only 3 October is confirmed, against https://www.gesetze-im-internet.de/einigvtr/art_2.html, read 2026-11-06
// (Einigungsvertrag, Article 2 Abs. 2: "Der 3. Oktober ist als Tag der Deutschen Einheit gesetzlicher Feiertag."). The plan's URL
// (einigungsvertrag/art_2.html) returned 404. The other eight days are set by the holiday laws of the 16 Länder, which were not
// read, so the claim that every Land keeps them is unverified. Nationwide days only; no moved days (Saturday and Sunday are not moved).

namespace Ballast.Time;

/// <summary>
/// The German nationwide public holidays. Days that only some Länder keep are not included.
/// </summary>
internal static class DeHolidays
{
    /// <summary>The German nationwide calendar.</summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "DE",
        null,
        "Nationwide public holidays; days that only some Länder keep (Epiphany, Corpus Christi, All Saints' Day and others) are not included.",
        [
            HolidayRule.Fixed(1, 1, "Neujahr", "New Year's Day"),
            HolidayRule.WesternEaster(-2, "Karfreitag", "Good Friday"),
            HolidayRule.WesternEaster(1, "Ostermontag", "Easter Monday"),
            HolidayRule.Fixed(5, 1, "Tag der Arbeit", "Labour Day"),
            HolidayRule.WesternEaster(39, "Christi Himmelfahrt", "Ascension Day"),
            HolidayRule.WesternEaster(50, "Pfingstmontag", "Whit Monday"),
            HolidayRule.Fixed(10, 3, "Tag der Deutschen Einheit", "German Unity Day"),
            HolidayRule.Fixed(12, 25, "Erster Weihnachtstag", "Christmas Day"),
            HolidayRule.Fixed(12, 26, "Zweiter Weihnachtstag", "Boxing Day"),
        ],
        sundayMovesToNextWorkingDay: false);
}
