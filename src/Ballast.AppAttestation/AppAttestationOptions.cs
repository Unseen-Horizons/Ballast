// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Text;
using System.Security.Cryptography;

namespace Ballast.AppAttestation;

/// <summary>
/// How strict the API is about who calls its data endpoints. Bound from the configuration section named by
/// <see cref="AppAttestationSetup.AttestationSection"/> (<c>AppAttestation</c> by default).
/// </summary>
public sealed class AppAttestationOptions
{
    /// <summary>
    /// False (the default, everywhere): a data call with no valid session is let through and logged as "would be required".
    /// True: it answers 401 with <see cref="AppAttestationSetup.SessionRequiredCode"/>. Turn it on only once no supported build can
    /// fail to attest.
    /// </summary>
    public bool Enforce { get; set; }

    /// <summary>
    /// Development only: <c>POST</c> of the session creates an unattested (<c>none</c>) session without reading the integrity token,
    /// so emulators, a desktop build and a local run work. It is ignored in every other environment, where startup logs an error
    /// when it is set.
    /// </summary>
    public bool AllowUnattested { get; set; }

    /// <summary>
    /// SHA-256 digests of the owner's own signing certificates (the debug key and the upload key), as <c>keytool -list -v</c> prints
    /// them (hex, colons optional) or as Play Integrity returns them (base64url). A build signed with one of them, on a genuine device,
    /// is accepted as the developer path. An entry that is not a SHA-256 digest is logged at startup and never matches.
    /// </summary>
    public string[] DeveloperCertificates { get; set; } = [];

    /// <summary>
    /// <see cref="DeveloperCertificates"/> normalised to base64url, which is how Play Integrity spells a digest. Entries that are not
    /// a SHA-256 digest are returned in <paramref name="rejected"/> so startup can say so instead of silently never matching.
    /// Comma- or semicolon-separated values in one entry are split, so one App Service setting can hold several.
    /// </summary>
    /// <param name="rejected">The entries that are not a SHA-256 digest, as written. Empty when every entry is valid.</param>
    /// <returns>The normalised digests, without duplicates.</returns>
    public HashSet<string> NormalizedDeveloperDigests(out List<string> rejected)
    {
        var digests = new HashSet<string>(StringComparer.Ordinal);
        rejected = [];
        foreach (var entry in DeveloperCertificates.SelectMany(e => e.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (NormalizeDigest(entry) is { } normalised)
                digests.Add(normalised);
            else
                rejected.Add(entry);
        }
        return digests;
    }

    /// <summary>
    /// A SHA-256 digest as base64url without padding, from hex (colons or spaces allowed) or from base64 or base64url. Null when the
    /// value is not 32 bytes.
    /// </summary>
    /// <param name="raw">The digest as written.</param>
    /// <returns>The digest in base64url, or null.</returns>
    public static string? NormalizeDigest(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0)
            return null;

        var hex = s.Replace(":", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (hex.Length == 64 && hex.All(Uri.IsHexDigit))
            return Base64Url.EncodeToString(Convert.FromHexString(hex));

        var b64 = s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        try
        {
            var bytes = Base64Url.DecodeFromChars(b64);
            return bytes.Length == SHA256.HashSizeInBytes ? Base64Url.EncodeToString(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
