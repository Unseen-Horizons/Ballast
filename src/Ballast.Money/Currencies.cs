// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Money;

/// <summary>
/// The shape of an ISO 4217 currency code, and the minor units (decimal places) of every code on ISO 4217 List One that has them.
/// </summary>
/// <remarks>
/// <para>
/// The table is ISO 4217 List One, published on 2026-09-17 by SIX, the ISO 4217 maintenance agency. It holds the 165 codes that
/// have minor units. The codes with none are left out, so <see cref="MinorUnits"/> gives <c>null</c> for them: the precious
/// metals XAG, XAU, XPD and XPT, the bond-market units XBA, XBB, XBC and XBD, and the unit and non-currency codes XDR, XSU, XTS,
/// XUA and XXX.
/// </para>
/// <para>
/// Two codes outside the list are kept: HRK, withdrawn on 2023-01-01, and BGN, withdrawn on 2026-01-01. They stay because records
/// priced in kuna and in lev still exist.
/// </para>
/// <para>
/// <see cref="IsIsoCode"/> checks only the shape: three ASCII letters. A code of the right shape that is not on the list, such as
/// XYZ, can still be held in a <see cref="Money"/>. It cannot be rounded, and <see cref="MoneyText"/> shows it with the decimal
/// places its value needs.
/// </para>
/// <para>
/// The table is fixed in this version of the library. A later version takes the next List One.
/// </para>
/// </remarks>
public static class Currencies
{
    private static readonly Dictionary<string, int> _minorUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AED"] = 2,
        ["AFN"] = 2,
        ["ALL"] = 2,
        ["AMD"] = 2,
        ["AOA"] = 2,
        ["ARS"] = 2,
        ["AUD"] = 2,
        ["AWG"] = 2,
        ["AZN"] = 2,
        ["BAM"] = 2,
        ["BBD"] = 2,
        ["BDT"] = 2,
        ["BHD"] = 3,
        ["BIF"] = 0,
        ["BMD"] = 2,
        ["BND"] = 2,
        ["BOB"] = 2,
        ["BOV"] = 2,
        ["BRL"] = 2,
        ["BSD"] = 2,
        ["BTN"] = 2,
        ["BWP"] = 2,
        ["BYN"] = 2,
        ["BZD"] = 2,
        ["CAD"] = 2,
        ["CDF"] = 2,
        ["CHE"] = 2,
        ["CHF"] = 2,
        ["CHW"] = 2,
        ["CLF"] = 4,
        ["CLP"] = 0,
        ["CNY"] = 2,
        ["COP"] = 2,
        ["COU"] = 2,
        ["CRC"] = 2,
        ["CUP"] = 2,
        ["CVE"] = 2,
        ["CZK"] = 2,
        ["DJF"] = 0,
        ["DKK"] = 2,
        ["DOP"] = 2,
        ["DZD"] = 2,
        ["EGP"] = 2,
        ["ERN"] = 2,
        ["ETB"] = 2,
        ["EUR"] = 2,
        ["FJD"] = 2,
        ["FKP"] = 2,
        ["GBP"] = 2,
        ["GEL"] = 2,
        ["GHS"] = 2,
        ["GIP"] = 2,
        ["GMD"] = 2,
        ["GNF"] = 0,
        ["GTQ"] = 2,
        ["GYD"] = 2,
        ["HKD"] = 2,
        ["HNL"] = 2,
        ["HTG"] = 2,
        ["HUF"] = 2,
        ["IDR"] = 2,
        ["ILS"] = 2,
        ["INR"] = 2,
        ["IQD"] = 3,
        ["IRR"] = 2,
        ["ISK"] = 0,
        ["JMD"] = 2,
        ["JOD"] = 3,
        ["JPY"] = 0,
        ["KES"] = 2,
        ["KGS"] = 2,
        ["KHR"] = 2,
        ["KMF"] = 0,
        ["KPW"] = 2,
        ["KRW"] = 0,
        ["KWD"] = 3,
        ["KYD"] = 2,
        ["KZT"] = 2,
        ["LAK"] = 2,
        ["LBP"] = 2,
        ["LKR"] = 2,
        ["LRD"] = 2,
        ["LSL"] = 2,
        ["LYD"] = 3,
        ["MAD"] = 2,
        ["MDL"] = 2,
        ["MGA"] = 2,
        ["MKD"] = 2,
        ["MMK"] = 2,
        ["MNT"] = 2,
        ["MOP"] = 2,
        ["MRU"] = 2,
        ["MUR"] = 2,
        ["MVR"] = 2,
        ["MWK"] = 2,
        ["MXN"] = 2,
        ["MXV"] = 2,
        ["MYR"] = 2,
        ["MZN"] = 2,
        ["NAD"] = 2,
        ["NGN"] = 2,
        ["NIO"] = 2,
        ["NOK"] = 2,
        ["NPR"] = 2,
        ["NZD"] = 2,
        ["OMR"] = 3,
        ["PAB"] = 2,
        ["PEN"] = 2,
        ["PGK"] = 2,
        ["PHP"] = 2,
        ["PKR"] = 2,
        ["PLN"] = 2,
        ["PYG"] = 0,
        ["QAR"] = 2,
        ["RON"] = 2,
        ["RSD"] = 2,
        ["RUB"] = 2,
        ["RWF"] = 0,
        ["SAR"] = 2,
        ["SBD"] = 2,
        ["SCR"] = 2,
        ["SDG"] = 2,
        ["SEK"] = 2,
        ["SGD"] = 2,
        ["SHP"] = 2,
        ["SLE"] = 2,
        ["SOS"] = 2,
        ["SRD"] = 2,
        ["SSP"] = 2,
        ["STN"] = 2,
        ["SVC"] = 2,
        ["SYP"] = 2,
        ["SZL"] = 2,
        ["THB"] = 2,
        ["TJS"] = 2,
        ["TMT"] = 2,
        ["TND"] = 3,
        ["TOP"] = 2,
        ["TRY"] = 2,
        ["TTD"] = 2,
        ["TWD"] = 2,
        ["TZS"] = 2,
        ["UAH"] = 2,
        ["UGX"] = 0,
        ["USD"] = 2,
        ["USN"] = 2,
        ["UYI"] = 0,
        ["UYU"] = 2,
        ["UYW"] = 4,
        ["UZS"] = 2,
        ["VED"] = 2,
        ["VES"] = 2,
        ["VND"] = 0,
        ["VUV"] = 0,
        ["WST"] = 2,
        ["XAD"] = 2,
        ["XAF"] = 0,
        ["XCD"] = 2,
        ["XCG"] = 2,
        ["XOF"] = 0,
        ["XPF"] = 0,
        ["YER"] = 2,
        ["ZAR"] = 2,
        ["ZMW"] = 2,
        ["ZWG"] = 2,
        // HRK is not on List One: withdrawn on 2023-01-01. It is kept because records priced in kuna still exist.
        ["HRK"] = 2,
        // BGN is not on List One: withdrawn on 2026-01-01; kept because records priced in lev still exist.
        ["BGN"] = 2,
    };

    /// <summary>
    /// Whether a code has the shape of an ISO 4217 alphabetic code: exactly three ASCII letters, in any case.
    /// </summary>
    /// <param name="code">The code to check. Null is not a code.</param>
    /// <returns>
    /// <c>true</c> for <c>EUR</c>, <c>eur</c> and <c>XYZ</c>; <c>false</c> for null, an empty string, a blank string, a code of the
    /// wrong length, or a character that is not an ASCII letter. It does not say whether the code is a real currency:
    /// use <see cref="MinorUnits"/> for that.
    /// </returns>
    public static bool IsIsoCode(string? code) =>
        code is { Length: 3 } && code.All(char.IsAsciiLetter);

    /// <summary>
    /// The number of minor units (decimal places) of a currency: 2 for EUR, 0 for JPY, 3 for KWD and 4 for CLF.
    /// </summary>
    /// <param name="code">The ISO 4217 code, in any case. Null, or a code outside the table, gives <c>null</c>.</param>
    /// <returns>
    /// The number of decimal places the currency is rounded to, or <c>null</c> when the library does not know it. A code that is
    /// not in the table is never assumed to have two decimal places.
    /// </returns>
    public static int? MinorUnits(string? code) =>
        code is not null && _minorUnits.TryGetValue(code, out var digits) ? digits : null;
}
