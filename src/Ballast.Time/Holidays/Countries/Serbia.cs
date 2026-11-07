// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Confirmed against https://www.paragraf.rs/propisi/zakon_o_drzavnim_i_drugim_praznicima_u_republici_srbiji.html, read 2026-11-06
// Law: Zakon o državnim i drugim praznicima u Republici Srbiji (Sl. glasnik RS 43/2001, 101/2007, 92/2011 as listed on that page).
// Articles read: 1 (Sretenje, 15 and 16 February), 1a (New Year 1-2 Jan, Labour Day 1-2 May, Victory Day 9 May, Armistice 11 Nov),
// 2 (Christmas 7 Jan, Orthodox Easter holidays from Good Friday to the second day of Easter), 3 (Victory Day is a working day),
// Article 2, item 2, quoted verbatim: "Vaskršnji praznici počev od Velikog petka zaključno sa drugim danom Vaskrsa"
// (Orthodox Easter holidays, beginning on Good Friday through the second day of Easter). That is why Holy Saturday and
// Easter Monday are listed. Read through the same legal database page, 2026-11-06.
// 3a (a state holiday that falls on a Sunday: the next working day is off), 5 (working commemorations, not days off).
// The source is a legal database that reproduces the gazette text; the official Sl. glasnik RS was not reached. Amendments
// after 92/2011 are not on that page and were not read.

namespace Ballast.Time;

/// <summary>Serbia's public holidays under the law on state and other holidays.</summary>
internal static class RsHolidays
{
    /// <summary>
    /// Serbia: days off under the Law on State and Other Holidays. Covers the state holidays and the Orthodox Christmas and
    /// Easter holidays. A state holiday that falls on a Sunday gives the next working day off. Victory Day (9 May) is a
    /// state holiday but a working day, so it is not included. The working commemorations (Saint Sava, Vidovdan and others)
    /// are not days off, so they are not included either. Holidays of other religious communities are not included.
    /// </summary>
    public static readonly RuleBasedCalendar Calendar = new(
        "RS",
        null,
        "National public holidays under the Law on State and Other Holidays: state holidays and the Orthodox Christmas and Easter holidays. " +
        "Victory Day (9 May, a working day), the working commemorations and the religious holidays of other communities are not included.",
        [
            // Rules: Fixed(month, day, local, english, substitutable); OrthodoxEaster(offset, local, english, substitutable).
            HolidayRule.Fixed(1, 1, "Nova godina", "New Year's Day", true),
            HolidayRule.Fixed(1, 2, "Nova godina", "New Year's Day (second day)", true),
            HolidayRule.Fixed(1, 7, "Božić", "Orthodox Christmas", false),
            HolidayRule.Fixed(2, 15, "Sretenje, Dan državnosti Srbije", "Statehood Day", true),
            HolidayRule.Fixed(2, 16, "Sretenje, Dan državnosti Srbije", "Statehood Day (second day)", true),
            HolidayRule.OrthodoxEaster(-2, "Veliki petak", "Orthodox Good Friday", false),
            HolidayRule.OrthodoxEaster(-1, "Velika subota", "Orthodox Holy Saturday", false),
            HolidayRule.OrthodoxEaster(0, "Vaskrs", "Orthodox Easter Sunday", false),
            HolidayRule.OrthodoxEaster(1, "Vaskršnji ponedeljak", "Orthodox Easter Monday", false),
            HolidayRule.Fixed(5, 1, "Praznik rada", "Labour Day", true),
            HolidayRule.Fixed(5, 2, "Praznik rada", "Labour Day (second day)", true),
            HolidayRule.Fixed(11, 11, "Dan primirja u Prvom svetskom ratu", "Armistice Day", true),
        ],
        true);
}
