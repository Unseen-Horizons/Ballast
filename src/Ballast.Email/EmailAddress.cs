// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net.Mail;

namespace Ballast.Email;

/// <summary>
/// Checks and masks e-mail addresses. <see cref="IsPlausible"/> is a shape check for a form, not a mailbox check: it asks for a
/// plain "name@domain.tld", with no display name and no spaces, and a dot in the domain. <see cref="Mask"/> is the form of an
/// address that a log may hold.
/// </summary>
public static class EmailAddress
{
    /// <summary>The length cap that <see cref="IsPlausible"/> uses unless the app gives another: 255 characters, the usual size of an e-mail column.</summary>
    public const int DefaultMaxLength = 255;

    /// <summary>
    /// True for an address that is already trimmed and has the plain shape, and is no longer than <paramref name="maxLength"/>.
    /// </summary>
    /// <param name="address">The address to check. Null, empty and blank values are not plausible.</param>
    /// <param name="maxLength">The longest address accepted, in characters. Must be at least 1.</param>
    /// <returns>True when the address has the plain shape and fits the cap.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is below 1.</exception>
    public static bool IsPlausible(string? address, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        if (string.IsNullOrEmpty(address) || address.Length > maxLength)
        {
            return false;
        }

        if (!MailAddress.TryCreate(address, out var mail) || !string.Equals(mail.Address, address, StringComparison.Ordinal))
        {
            return false;
        }

        var host = mail.Host;
        return host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.') && !host.Contains("..") && !host.Contains('[');
    }

    /// <summary>
    /// The address with its name hidden, for a log: the first character of the name, three stars, and the domain. A log can
    /// then say which provider domain a mail went to without holding the person's address.
    /// </summary>
    /// <param name="address">The address to mask.</param>
    /// <returns>
    /// The masked form, for example <c>k***@example.test</c>. A value with no <c>@</c> is masked to <c>***</c>, so nothing of it
    /// is shown.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    public static string Mask(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var at = address.LastIndexOf('@');
        if (at < 0)
        {
            return "***";
        }

        var name = address[..at];
        var domain = address[(at + 1)..];
        var first = name.Length > 0 ? name[..1] : string.Empty;
        return $"{first}***@{domain}";
    }
}
