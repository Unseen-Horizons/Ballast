// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO.Compression;
using System.Text;

using Ballast.Storage;

namespace Ballast.Storage.Tests;

/// <summary>
/// The binary signatures, including the cases the applications' own checks covered: a PDF renamed as a photo, a real archive, the
/// RIFF containers that are not WebP, and an MP4 that shares the ISO header. Where the applications' checks differed, the stricter
/// signature is tested (the full PNG header, the dash after %PDF). The text formats are left out: an application reads those itself.
/// </summary>
public class FileKindsTests
{
    /// <summary>The header the upload path passes: the first 64 KB, padded with zeros, as the sniffers are given it.</summary>
    private const int UploadHeaderBytes = 65536;

    private static byte[] Header(params byte[] bytes)
    {
        var buffer = new byte[UploadHeaderBytes];
        bytes.CopyTo(buffer, 0);
        return buffer;
    }

    /// <summary>An ISO base media file header: a box size, "ftyp", then the brand, then padding.</summary>
    private static byte[] IsoMedia(string brand) =>
        [0, 0, 0, 0x18, .. Encoding.ASCII.GetBytes("ftyp"), .. Encoding.ASCII.GetBytes(brand), .. new byte[16]];

    public static TheoryData<byte[], FileKind, string, string> KnownSignatures => new()
    {
        { [0xFF, 0xD8, 0xFF, 0xE0], FileKind.Jpeg, "image/jpeg", "jpg" },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], FileKind.Png, "image/png", "png" },
        { Encoding.ASCII.GetBytes("%PDF-1.7\n%test"), FileKind.Pdf, "application/pdf", "pdf" },
        { [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00], FileKind.Zip, "application/zip", "zip" },
        { Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 "), FileKind.Webp, "image/webp", "webp" },
        { IsoMedia("heic"), FileKind.Heic, "image/heic", "heic" }
    };

    [Theory]
    [MemberData(nameof(KnownSignatures))]
    public void Each_known_signature_gives_its_kind_media_type_and_extension(byte[] bytes, FileKind kind, string mediaType, string extension)
    {
        var detected = FileKinds.Detect(bytes);

        Assert.Equal(new FileKindInfo(kind, mediaType, extension), detected);
    }

    [Fact]
    public void A_padded_upload_header_is_read_the_same_way()
    {
        var jpeg = FileKinds.Detect(Header(0xFF, 0xD8, 0xFF, 0xE0));
        var png = FileKinds.Detect(Header(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A));
        var pdf = FileKinds.Detect(Header(Encoding.ASCII.GetBytes("%PDF-1.4 pretending to be a photo")));

        Assert.Equal(FileKind.Jpeg, jpeg?.Kind);
        Assert.Equal(FileKind.Png, png?.Kind);
        Assert.Equal(FileKind.Pdf, pdf?.Kind);
    }

    [Fact]
    public void A_real_zip_archive_is_read_as_zip_whatever_it_holds()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("whatever.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not a pass");
        }

        Assert.Equal(FileKind.Zip, FileKinds.Detect(buffer.ToArray())?.Kind);
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("heix")]
    [InlineData("mif1")]
    [InlineData("hevc")]
    [InlineData("msf1")]
    [InlineData("heim")]
    [InlineData("heis")]
    [InlineData("HEIC")]
    public void Each_heic_or_heif_brand_is_read_as_heic_whatever_its_case(string brand)
    {
        Assert.Equal(FileKind.Heic, FileKinds.Detect(IsoMedia(brand))?.Kind);
    }

    [Fact]
    public void An_mp4_is_not_an_image_and_is_not_recognised()
    {
        Assert.Null(FileKinds.Detect(IsoMedia("isom")));
    }

    [Fact]
    public void A_riff_container_that_is_not_webp_is_not_recognised()
    {
        Assert.Null(FileKinds.Detect(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ")));
    }

    [Fact]
    public void A_png_with_only_the_first_four_bytes_of_its_signature_is_not_recognised()
    {
        // The stricter check: the full eight-byte signature, not the first four.
        Assert.Null(FileKinds.Detect([0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x00, 0x00]));
    }

    [Fact]
    public void A_pdf_without_the_dash_after_its_name_is_not_recognised()
    {
        Assert.Null(FileKinds.Detect(Encoding.ASCII.GetBytes("%PDF")));
    }

    [Fact]
    public void An_empty_zip_archive_with_no_entries_is_not_recognised()
    {
        // The end-of-central-directory signature alone: only a local entry header is the signature checked.
        Assert.Null(FileKinds.Detect([0x50, 0x4B, 0x05, 0x06]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("just some text")]
    [InlineData("<html><body>nope</body></html>")]
    [InlineData("#!/bin/sh\nrm -rf /")]
    [InlineData("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n")]
    [InlineData("From: a@example.test\r\nMIME-Version: 1.0\r\n\r\nhello")]
    public void Text_and_unknown_content_is_not_recognised(string content)
    {
        Assert.Null(FileKinds.Detect(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void A_truncated_header_is_not_recognised_and_does_not_throw()
    {
        Assert.Null(FileKinds.Detect([]));
        Assert.Null(FileKinds.Detect([0xFF]));
        Assert.Null(FileKinds.Detect([0x52, 0x49, 0x46, 0x46]));
        Assert.Null(FileKinds.Detect("RIFF\0\0\0\0"u8));

        // A box size and "ftyp" with no brand after them: eight bytes, too few for the brand.
        Assert.Null(FileKinds.Detect(IsoMedia("heic").AsSpan(0, 8)));
    }

    [Fact]
    public void The_media_type_and_extension_of_a_kind_come_without_a_leading_dot()
    {
        var jpeg = FileKinds.Detect([0xFF, 0xD8, 0xFF]);

        Assert.NotNull(jpeg);
        Assert.Equal("jpg", jpeg.Value.Extension);
        Assert.DoesNotContain('.', jpeg.Value.Extension);
    }
}
