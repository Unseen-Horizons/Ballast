// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Storage;

namespace Ballast.Storage.Tests;

/// <summary>
/// The key rule. Every refused shape of the strictest copy is here, each with the reason it must give, and each device name
/// and a few safe keys that look close to the refused ones.
/// </summary>
public class SafePathTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ballast-safe-root");

    [Theory]
    [InlineData("../evil.txt", SafePathReason.DotSegment)]
    [InlineData("../uploads-evil/a.png", SafePathReason.DotSegment)]
    [InlineData("uploads/../../evil.txt", SafePathReason.DotSegment)]
    [InlineData("uploads/../../../evil.txt", SafePathReason.DotSegment)]
    [InlineData("..", SafePathReason.DotSegment)]
    [InlineData(".", SafePathReason.DotSegment)]
    [InlineData("uploads/./a.png", SafePathReason.DotSegment)]
    [InlineData("uploads/a/..", SafePathReason.DotSegment)]
    [InlineData("/etc/passwd", SafePathReason.RootedKey)]
    [InlineData("/evil.txt", SafePathReason.RootedKey)]
    [InlineData(@"C:\Windows\evil.txt", SafePathReason.Backslash)]
    [InlineData(@"uploads\a.png", SafePathReason.Backslash)]
    [InlineData(@"..\evil.txt", SafePathReason.Backslash)]
    [InlineData("C:evil.txt", SafePathReason.Colon)]
    [InlineData("uploads//a.png", SafePathReason.EmptySegment)]
    [InlineData("uploads/", SafePathReason.EmptySegment)]
    [InlineData("uploads/a.png ", SafePathReason.SurroundingSpace)]
    [InlineData(" uploads/a.png", SafePathReason.SurroundingSpace)]
    [InlineData("uploads/ a.png", SafePathReason.SurroundingSpace)]
    [InlineData("uploads/a.b. ", SafePathReason.SurroundingSpace)]
    [InlineData("uploads/a.", SafePathReason.TrailingDot)]
    [InlineData("uploads/a.b.", SafePathReason.TrailingDot)]
    [InlineData("uploads/a<b>.png", SafePathReason.ReservedCharacter)]
    [InlineData("uploads/a|b.png", SafePathReason.ReservedCharacter)]
    [InlineData("uploads/a?b.png", SafePathReason.ReservedCharacter)]
    [InlineData("uploads/a*b.png", SafePathReason.ReservedCharacter)]
    [InlineData("uploads/a\"b.png", SafePathReason.ReservedCharacter)]
    [InlineData("uploads/a\0b.png", SafePathReason.ControlCharacter)]
    [InlineData("uploads/a\tb.png", SafePathReason.ControlCharacter)]
    [InlineData("uploads/NUL.txt", SafePathReason.ReservedName)]
    [InlineData("con", SafePathReason.ReservedName)]
    [InlineData("uploads/lpt9.log", SafePathReason.ReservedName)]
    [InlineData("COM1", SafePathReason.ReservedName)]
    [InlineData("uploads/aux.tar.gz", SafePathReason.ReservedName)]
    public void A_refused_key_names_its_reason(string key, SafePathReason reason)
    {
        var ex = Assert.Throws<SafePathException>(() => SafePath.Resolve(Root, key));

        Assert.Equal(reason, ex.Reason);
        Assert.Equal("key", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_key_is_refused_as_empty(string? key)
    {
        var ex = Assert.Throws<SafePathException>(() => SafePath.Resolve(Root, key!));

        Assert.Equal(SafePathReason.Empty, ex.Reason);
    }

    [Theory]
    [InlineData("receipt.txt", "receipt.txt")]
    [InlineData("uploads/2026/10/abc.jpg", "uploads/2026/10/abc.jpg")]
    [InlineData(".hidden", ".hidden")]
    [InlineData("a..b", "a..b")]
    [InlineData("report 2026 (final).csv", "report 2026 (final).csv")]
    [InlineData("COM10.log", "COM10.log")]
    [InlineData("CONSOLE.txt", "CONSOLE.txt")]
    public void A_safe_key_resolves_to_a_path_under_the_root(string key, string relative)
    {
        var full = SafePath.Resolve(Root, key);

        Assert.Equal(Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar))), full);
        Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, full, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_root_that_is_a_file_system_root_still_holds_its_keys()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Equal(Path.Combine(root, "a.txt"), SafePath.Resolve(root, "a.txt"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_root_is_refused(string? root)
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => SafePath.Resolve(root!, "a.txt"));

        Assert.Equal("root", ex.ParamName);
    }

    [Fact]
    public void Every_reason_has_its_own_message()
    {
        var messages = Enum.GetValues<SafePathReason>().Select(reason => new SafePathException(reason).Message).ToArray();

        Assert.Equal(messages.Length, messages.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(messages, message => message.Contains("not a safe relative path", StringComparison.Ordinal));
    }
}
