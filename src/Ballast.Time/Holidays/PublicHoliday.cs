// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time;

/// <summary>
/// One public holiday or day off on a calendar date.
/// </summary>
/// <param name="Date">The calendar date. It is a plain date in the country's own calendar, with no zone.</param>
/// <param name="LocalName">The name in the country's language, in its own script (Cyrillic for the Macedonian calendar).</param>
/// <param name="EnglishName">The English name. A day in lieu says so in its name.</param>
/// <param name="IsSubstituteDay">
/// True for a day off that stands in for a holiday that fell on a Sunday. Its <paramref name="LocalName"/> is the
/// holiday's own name.
/// </param>
public sealed record PublicHoliday(DateOnly Date, string LocalName, string EnglishName, bool IsSubstituteDay = false);
