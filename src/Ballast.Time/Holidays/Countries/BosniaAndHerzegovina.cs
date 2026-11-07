// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Republika Srpska: NOT REGISTERED. The rows were read against https://www.paragraf.ba/propisi/republika-srpska/zakon-o-praznicima-republike-srpske.html,
// read 2026-11-06. Law: Zakon o praznicima Republike Srpske (Sl. glasnik RS 43/2007), Articles 2, 3, 4, 7 and 8 read. Article 3(b)
// (Dan Republike, 9 January) is shown there as "prestalo da važi" (ceased to be in force), and the Constitutional Court of BiH
// (decision U 3/13, 26 November 2015, https://www.ustavnisud.ba/uploads/odluke/_bs/U-3-13-1017392.pdf, read 2026-11-06) found
// that Article 3(b) is not in line with the Constitution. The Paragraf page is a legal database's copy of the consolidated
// text; the official Sl. glasnik RS was not reached, and amendments after that text were not read.
//
// Federation of BiH: NOT CONFIRMED. The federal holiday law that the sources name (SFRJ Law on Holidays, Sl. list SFRJ 6/73,
// and RBiH Law, Sl. glasnik RBiH 2/92 and 13/94) was not read, and Sl. novine FBiH was not reached. The rows follow news and
// ministry notices only. Cantonal and religious days are not included.
//
// State: NOT CONFIRMED. No state law listing the holidays was found; the searches reached only secondary sources. The 1 March
// and 25 November days rest on 1995 proclamation laws (Sl. list RBiH 9/95 is cited for 1 March) whose text was not read.
// Sources disagree on whether 1 March moves when it falls on a Sunday (the FBiH ministry says 2026 is not moved; it says
// earlier years were).

namespace Ballast.Time;

/// <summary>
/// Bosnia and Herzegovina, by level: the state, the Federation of Bosnia and Herzegovina (FBiH) and Republika Srpska (RS).
/// The Brčko District is not covered.
/// </summary>
internal static class BaHolidays
{
    /// <summary>
    /// The state-level days. NOT CONFIRMED (see the file header): only the two proclamation days are listed.
    /// </summary>
    public static readonly RuleBasedCalendar State = new(
        "BA",
        null,
        "Days that apply to the whole state under the two laws that proclaim them (1 March and 25 November). No state law " +
        "lists all holidays, so the entities' days (New Year, Labour Day and the rest) and the religious days are not included.",
        [
            HolidayRule.Fixed(3, 1, "Dan nezavisnosti Bosne i Hercegovine", "Independence Day of Bosnia and Herzegovina"),
            HolidayRule.Fixed(11, 25, "Dan državnosti Bosne i Hercegovine", "Statehood Day of Bosnia and Herzegovina"),
        ],
        false);

    /// <summary>
    /// The Federation of Bosnia and Herzegovina. NOT CONFIRMED (see the file header).
    /// </summary>
    public static readonly RuleBasedCalendar Federation = new(
        "BA",
        "FBIH",
        "Days that the Federation's holiday rules and the state proclamation days make days off in the Federation. Cantonal " +
        "days and religious holidays are not included.",
        [
            HolidayRule.Fixed(1, 1, "Nova godina", "New Year's Day", true),
            HolidayRule.Fixed(1, 2, "Nova godina", "New Year's Day (second day)", true),
            HolidayRule.Fixed(3, 1, "Dan nezavisnosti Bosne i Hercegovine", "Independence Day of Bosnia and Herzegovina"),
            HolidayRule.Fixed(5, 1, "Praznik rada", "Labour Day", true),
            HolidayRule.Fixed(5, 2, "Praznik rada", "Labour Day (second day)", true),
            HolidayRule.Fixed(11, 25, "Dan državnosti Bosne i Hercegovine", "Statehood Day of Bosnia and Herzegovina"),
        ],
        true);

    /// <summary>
    /// Republika Srpska: the republic holidays and the religious holidays that Articles 7 and 8 of the law list. Not registered:
    /// the Sunday move and the believers-only religious days are not confirmed.
    /// </summary>
    public static readonly RuleBasedCalendar RepublikaSrpska = new(
        "BA",
        "RS",
        "The republic holidays and the religious holidays of Articles 2, 7 and 8 of the Law on Holidays of Republika Srpska. " +
        "The religious days are days off for believers, by paid absence (Article 8(3) and (4)), and are listed as holidays. " +
        "Dan Republike (9 January) is not included: Article 3(b) has ceased to apply. Lunar Islamic holidays (Kurban-bajram, " +
        "Ramazan-bajram) are not included. A second day of New Year or of 1 and 2 May that falls on a Sunday moves to the next day.",
        [
            HolidayRule.Fixed(1, 1, "Nova godina", "New Year's Day"),
            HolidayRule.Fixed(1, 2, "Nova godina", "New Year's Day (second day)", true),
            HolidayRule.Fixed(1, 6, "Badnji dan (pravoslavni)", "Orthodox Christmas Eve"),
            HolidayRule.Fixed(1, 7, "Božić (pravoslavni)", "Orthodox Christmas"),
            HolidayRule.Fixed(5, 1, "Međunarodni praznik rada", "International Labour Day"),
            HolidayRule.Fixed(5, 2, "Međunarodni praznik rada (drugi dan)", "International Labour Day (second day)", true),
            HolidayRule.Fixed(5, 9, "Dan pobjede nad fašizmom", "Victory over Fascism Day"),
            HolidayRule.Fixed(
                11,
                21,
                "Dan uspostavljanja Opšteg okvirnog sporazuma za mir u Bosni i Hercegovini",
                "Day of the General Framework Agreement for Peace in Bosnia and Herzegovina"),
            HolidayRule.Fixed(12, 24, "Badnji dan (katolički)", "Catholic Christmas Eve"),
            HolidayRule.Fixed(12, 25, "Božić (katolički)", "Catholic Christmas"),
            HolidayRule.WesternEaster(-2, "Veliki petak (katolički)", "Catholic Good Friday"),
            HolidayRule.WesternEaster(0, "Uskrs (katolički)", "Catholic Easter Sunday"),
            HolidayRule.WesternEaster(1, "Uskršnji ponedjeljak (katolički)", "Catholic Easter Monday"),
            HolidayRule.OrthodoxEaster(-2, "Veliki petak (pravoslavni)", "Orthodox Good Friday"),
            HolidayRule.OrthodoxEaster(0, "Vaskrs (pravoslavni)", "Orthodox Easter Sunday"),
            HolidayRule.OrthodoxEaster(1, "Vaskršnji ponedjeljak (pravoslavni)", "Orthodox Easter Monday"),
        ],
        true);
}
