// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// NOT CONFIRMED: https://www.slov-lex.sk/ezbierky/pravne-predpisy/SK/ZZ/1993/241/ returned only the Act's title
// ("241/1993 Zb."), and the redirect to redir.slov-lex.sk was not followed to its body text, so no row below is checked
// against the law. In particular 24 December (Štedrý deň) as a day off is unverified. This class is NOT registered in
// PublicHolidayCalendar, so For("SK") returns null until the text is confirmed.

namespace Ballast.Time;

/// <summary>
/// The state holidays and days of rest of Slovakia under Act No. 241/1993 Z. z. The table is not yet confirmed (see the
/// header), and the class is not registered.
/// </summary>
internal static class SkHolidays
{
    /// <summary>The Slovak national calendar (Region is null). Not registered until confirmed.</summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "SK",
        region: null,
        scope: "State holidays and days of rest under Act No. 241/1993 Z. z. (not yet confirmed against the Act's text).",
        rules:
        [
            HolidayRule.Fixed(1, 1, "Deň vzniku Slovenskej republiky", "Day of the Establishment of the Slovak Republic"),
            HolidayRule.Fixed(1, 6, "Zjavenie Pána (Traja králi)", "Epiphany"),
            HolidayRule.WesternEaster(-2, "Veľký piatok", "Good Friday"),
            HolidayRule.WesternEaster(1, "Veľkonočný pondelok", "Easter Monday"),
            HolidayRule.Fixed(5, 1, "Sviatok práce", "Labour Day"),
            HolidayRule.Fixed(5, 8, "Deň víťazstva nad fašizmom", "Victory over Fascism Day"),
            HolidayRule.Fixed(7, 5, "Sviatok svätého Cyrila a Metoda", "Saints Cyril and Methodius Day"),
            HolidayRule.Fixed(8, 29, "Výročie Slovenského národného povstania", "Anniversary of the Slovak National Uprising"),
            HolidayRule.Fixed(9, 1, "Deň Ústavy Slovenskej republiky", "Constitution Day"),
            HolidayRule.Fixed(9, 15, "Sedembolestná Panna Mária", "Our Lady of Sorrows"),
            HolidayRule.Fixed(11, 1, "Sviatok všetkých svätých", "All Saints' Day"),
            HolidayRule.Fixed(11, 17, "Deň boja za slobodu a demokraciu", "Struggle for Freedom and Democracy Day"),
            HolidayRule.Fixed(12, 24, "Štedrý deň", "Christmas Eve"),
            HolidayRule.Fixed(12, 25, "Prvý sviatok vianočný", "Christmas Day"),
            HolidayRule.Fixed(12, 26, "Druhý sviatok vianočný", "Boxing Day"),
        ],
        sundayMovesToNextWorkingDay: false);
}
