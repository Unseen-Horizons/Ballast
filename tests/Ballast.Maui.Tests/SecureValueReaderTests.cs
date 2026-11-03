// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>
/// A secure value that cannot be read (a restored phone, whose key did not come back) is treated as absent, the store is cleared once, and nothing is substituted for the value.
/// </summary>
public sealed class SecureValueReaderTests
{
    [Fact]
    public async Task A_readable_value_is_returned_and_nothing_is_cleared()
    {
        var values = new FakeSecureValues();
        await values.SetAsync("auth_token", "abc");
        var reader = new SecureValueReader(values, new RecordingLog());

        Assert.Equal("abc", await reader.GetAsync("auth_token"));
        Assert.Equal(0, values.RemoveAllCalls);
    }

    [Fact]
    public async Task An_unreadable_value_is_absent_and_the_store_is_cleared_once()
    {
        var values = new FakeSecureValues();
        await values.SetAsync("auth_token", "abc");
        values.ReadFault = new InvalidOperationException("keystore key not restored");
        var log = new RecordingLog();
        var reader = new SecureValueReader(values, log);

        Assert.Null(await reader.GetAsync("auth_token"));
        Assert.Null(await reader.GetAsync("refresh_token"));

        Assert.Equal(1, values.RemoveAllCalls);
        Assert.Null(values.Peek("auth_token"));
        Assert.Equal(2, log.Warnings.Count);
    }

    [Fact]
    public async Task Rearming_lets_the_next_failure_clear_the_store_again()
    {
        var values = new FakeSecureValues { ReadFault = new InvalidOperationException("keystore key not restored") };
        var reader = new SecureValueReader(values, new RecordingLog());
        await reader.GetAsync("auth_token");

        reader.RearmClear();
        await reader.GetAsync("auth_token");

        Assert.Equal(2, values.RemoveAllCalls);
    }

    [Fact]
    public async Task A_cancelled_read_propagates_the_cancellation()
    {
        var values = new FakeSecureValues { ReadDelay = TimeSpan.FromSeconds(5) };
        var reader = new SecureValueReader(values, new RecordingLog());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.GetAsync("auth_token", cancelled.Token));
    }
}
