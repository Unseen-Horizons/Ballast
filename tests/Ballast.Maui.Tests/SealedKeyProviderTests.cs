// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;

using Ballast.Maui.Diagnostics;
using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>
/// The one cache key of an app's on-device stores: made once, kept in the secure store, shared by every caller, and never overwritten by a second one.
/// </summary>
public sealed class SealedKeyProviderTests
{
    private const string KeyName = "test.cacheKey";

    private static (FakeSecureValues Values, SecureValueReader Reader) Store()
    {
        var values = new FakeSecureValues();
        return (values, new SecureValueReader(values, new RecordingLog()));
    }

    [Fact]
    public async Task One_key_is_shared_and_is_the_one_persisted()
    {
        var (values, reader) = Store();
        using var keys = new SealedKeyProvider(values, reader, KeyName);

        var first = await keys.GetKeyAsync();
        var second = await keys.GetKeyAsync();

        Assert.Equal(SealedKeyProvider.KeyBytes, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(1, values.Writes);
        Assert.Equal(Convert.ToBase64String(first), values.Peek(KeyName));

        var sealedBytes = SealedPayload.Seal(first, "{\"ticket\":1}");
        Assert.Equal("{\"ticket\":1}", SealedPayload.Open(second, sealedBytes));
    }

    [Fact]
    public async Task Concurrent_first_use_mints_the_key_once()
    {
        // Slow reads hold every caller inside the "no key yet" window; without the lock each one would make its own key.
        var (values, reader) = Store();
        values.ReadDelay = TimeSpan.FromMilliseconds(30);
        using var keys = new SealedKeyProvider(values, reader, KeyName);

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => keys.GetKeyAsync())));

        Assert.Equal(1, values.Writes);
        Assert.All(results, key => Assert.Equal(results[0], key));
        Assert.Equal(Convert.ToBase64String(results[0]), values.Peek(KeyName));
    }

    [Fact]
    public async Task A_key_already_in_the_secure_store_is_adopted_and_never_overwritten()
    {
        var (values, reader) = Store();
        var existing = Enumerable.Range(1, SealedKeyProvider.KeyBytes).Select(i => (byte)i).ToArray();
        await values.SetAsync(KeyName, Convert.ToBase64String(existing));
        var writesBefore = values.Writes;
        using var keys = new SealedKeyProvider(values, reader, KeyName);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => keys.GetKeyAsync())));

        Assert.All(results, key => Assert.Equal(existing, key));
        Assert.Equal(writesBefore, values.Writes);
    }

    [Fact]
    public async Task Clearing_the_key_mints_a_new_one_and_leaves_old_files_unreadable()
    {
        var (values, reader) = Store();
        using var keys = new SealedKeyProvider(values, reader, KeyName);
        var oldKey = await keys.GetKeyAsync();
        var oldFile = SealedPayload.Seal(oldKey, "secret");

        await keys.ClearAsync();
        Assert.Null(values.Peek(KeyName));

        var newKey = await keys.GetKeyAsync();
        Assert.NotEqual(oldKey, newKey);
        Assert.ThrowsAny<CryptographicException>(() => SealedPayload.Open(newKey, oldFile));
    }

    [Fact]
    public async Task An_unreadable_stored_key_is_made_afresh_and_the_store_is_cleared_once()
    {
        var (values, reader) = Store();
        values.ReadFault = new InvalidOperationException("keystore key not restored");
        using var keys = new SealedKeyProvider(values, reader, KeyName);

        var key = await keys.GetKeyAsync();

        Assert.Equal(SealedKeyProvider.KeyBytes, key.Length);
        Assert.Equal(Convert.ToBase64String(key), values.Peek(KeyName));
        Assert.Equal(1, values.RemoveAllCalls);
    }

    [Fact]
    public async Task A_stored_key_that_is_not_base64_throws_and_is_never_replaced()
    {
        var (values, reader) = Store();
        await values.SetAsync(KeyName, "not base64!!");
        using var keys = new SealedKeyProvider(values, reader, KeyName);

        await Assert.ThrowsAsync<FormatException>(() => keys.GetKeyAsync());

        Assert.Equal("not base64!!", values.Peek(KeyName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_key_name_is_refused(string name)
    {
        var (values, reader) = Store();

        Assert.Throws<ArgumentException>(() => new SealedKeyProvider(values, reader, name));
    }
}
