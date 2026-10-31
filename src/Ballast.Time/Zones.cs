// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Ballast.Time;

/// <summary>
/// Finds the <see cref="TimeZoneInfo"/> that a zone id names. An id can be an IANA id (<c>Europe/Zagreb</c>) or a
/// Windows id (<c>Central European Standard Time</c>), and both are accepted on every machine.
/// </summary>
/// <remarks>
/// <para>
/// An id that is null, blank, not shaped like a zone id, or not a zone this machine knows gives <c>null</c>. The caller
/// decides what that means. The library never guesses a zone, and never falls back to UTC or to the machine's zone.
/// </para>
/// <para>
/// Nothing is cached. Each call looks the id up, and an id that is not known is not kept anywhere, so a stream of
/// unknown ids cannot make memory grow.
/// </para>
/// </remarks>
public static partial class Zones
{
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+\-/ ().]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ZoneIdShape();

    /// <summary>
    /// Finds the zone that <paramref name="zoneId"/> names.
    /// </summary>
    /// <param name="zoneId">An IANA id or a Windows id. Surrounding whitespace is ignored.</param>
    /// <returns>
    /// The zone, or <c>null</c> when <paramref name="zoneId"/> is null or blank, when it is not shaped like a zone id
    /// (letters, digits and <c>_ + - / ( ) .</c> and spaces, a first letter, at most 64 characters, no <c>..</c>), or when
    /// this machine does not know it.
    /// </returns>
    /// <remarks>
    /// Never throws. The shape is checked before the id is looked up, so an id is never handed to the file system as a
    /// path.
    /// </remarks>
    public static TimeZoneInfo? Find(string? zoneId)
    {
        var id = Shaped(zoneId);
        return id is null ? null : Resolve(id);
    }

    /// <summary>
    /// The id to store for the zone that <paramref name="zoneId"/> names: its IANA id. A Windows id is converted to the
    /// IANA id this machine maps it to. An IANA id is kept as written, without surrounding whitespace.
    /// </summary>
    /// <param name="zoneId">An IANA id or a Windows id.</param>
    /// <returns>
    /// The IANA id, or <c>null</c> when <see cref="Find(string?)"/> does not find the zone. An unknown id is not kept as
    /// written, so a stored zone is always one this machine knows.
    /// </returns>
    public static string? Canonical(string? zoneId)
    {
        var id = Shaped(zoneId);
        if (id is null || Resolve(id) is null)
            return null;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && iana is not null ? iana : id;
    }

    /// <summary>The trimmed id when it has the shape of a zone id, otherwise null.</summary>
    private static string? Shaped(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
            return null;
        var id = zoneId.Trim();
        return ZoneIdShape().IsMatch(id) && !id.Contains("..", StringComparison.Ordinal) ? id : null;
    }

    /// <summary>Looks the id up as given, then as the other kind of id this machine can map it to.</summary>
    private static TimeZoneInfo? Resolve(string id)
    {
        var direct = TryFind(id);
        if (direct is not null)
            return direct;

        // A Windows id on a machine that only knows IANA ids, and the other way round.
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && iana is not null)
        {
            var viaIana = TryFind(iana);
            if (viaIana is not null)
                return viaIana;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && windows is not null)
            return TryFind(windows);

        return null;
    }

    private static TimeZoneInfo? TryFind(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return null;
        }
    }
}
