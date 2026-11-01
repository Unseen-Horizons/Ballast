// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace Ballast.Storage;

/// <summary>
/// Recognises a few binary file types from their first bytes (their signatures), so that a file is judged by what it is, and
/// never by its name or by the content type its sender gave. The types are JPEG, PNG, WebP, PDF, ZIP and HEIC/HEIF. A text format
/// has no signature, so an application that needs one reads it itself.
/// </summary>
/// <remarks>
/// Each signature is in the first 12 bytes. WebP and HEIC need all 12, so a shorter header is not recognised as either. The
/// signatures are written here by hand and not read from a package, so the library has no dependency and nothing that a trimmed
/// or ahead-of-time build cannot use. Where the apps' checks differed, this takes the stricter signature: PNG compares all eight
/// of its bytes, and PDF needs the dash after <c>%PDF</c>.
/// </remarks>
public static class FileKinds
{
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();
    private static readonly byte[] WebpSignature = "WEBP"u8.ToArray();
    private static readonly byte[] FtypBox = "ftyp"u8.ToArray();

    /// <summary>The image brands of an ISO base media file that make it HEIC or HEIF, rather than, say, an MP4 video.</summary>
    private static readonly byte[][] HeicBrands =
    [
        "heic"u8.ToArray(), "heix"u8.ToArray(), "hevc"u8.ToArray(), "hevx"u8.ToArray(),
        "mif1"u8.ToArray(), "msf1"u8.ToArray(), "heim"u8.ToArray(), "heis"u8.ToArray()
    ];

    private static readonly FileKindInfo Jpeg = new(FileKind.Jpeg, "image/jpeg", "jpg");
    private static readonly FileKindInfo Png = new(FileKind.Png, "image/png", "png");
    private static readonly FileKindInfo Webp = new(FileKind.Webp, "image/webp", "webp");
    private static readonly FileKindInfo Pdf = new(FileKind.Pdf, "application/pdf", "pdf");
    private static readonly FileKindInfo Zip = new(FileKind.Zip, "application/zip", "zip");
    private static readonly FileKindInfo Heic = new(FileKind.Heic, "image/heic", "heic");

    /// <summary>
    /// Reads the type of a file from its first bytes. The caller passes the start of the file: the first 12 bytes are enough for
    /// every type, and more do no harm.
    /// </summary>
    /// <param name="header">The first bytes of the file. An empty or short span is accepted, and is not an error.</param>
    /// <returns>The type, or null when the bytes match none of the recognised signatures.</returns>
    public static FileKindInfo? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegSignature))
        {
            return Jpeg;
        }

        if (header.StartsWith(PngSignature))
        {
            return Png;
        }

        if (header.StartsWith(PdfSignature))
        {
            return Pdf;
        }

        if (header.StartsWith(ZipSignature))
        {
            return Zip;
        }

        // A RIFF container holds its real format from byte 8: WAVE and AVI share the RIFF start with WebP.
        if (header.Length >= 12 && header.StartsWith(RiffSignature) && header[8..12].SequenceEqual(WebpSignature))
        {
            return Webp;
        }

        // An ISO base media file has a box size, then "ftyp", then the brand that says what it holds.
        if (header.Length >= 12 && header[4..8].SequenceEqual(FtypBox) && IsHeicBrand(header[8..12]))
        {
            return Heic;
        }

        return null;
    }

    private static bool IsHeicBrand(ReadOnlySpan<byte> brand)
    {
        foreach (var known in HeicBrands)
        {
            if (Ascii.EqualsIgnoreCase(brand, known))
            {
                return true;
            }
        }

        return false;
    }
}
