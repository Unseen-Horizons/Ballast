// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Connectivity;

namespace Ballast.Maui.Tests;

/// <summary>
/// The app's one live connectivity state: it starts from the current value, follows changes both ways, holds exactly one subscription, and stops listening when disposed.
/// </summary>
public sealed class OfflineModeStateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Starts_from_the_current_value(bool initial)
    {
        using var state = new OfflineModeState(new FakeConnectivity { Offline = initial });

        Assert.Equal(initial, state.IsOffline);
        Assert.False(state.IsExpanded);
    }

    [Fact]
    public void Follows_changes_both_ways()
    {
        // The state hops transitions back onto the thread that built it. The test runner installs its own SynchronizationContext,
        // which never pumps posted work mid-test, so the state is built where transitions apply inline.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var source = new FakeConnectivity();
            using var state = new OfflineModeState(source);

            source.Raise(true);
            Assert.True(state.IsOffline);

            source.Raise(false);
            Assert.False(state.IsOffline);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void Only_one_subscription_exists_and_reading_or_toggling_adds_none()
    {
        var source = new FakeConnectivity();
        using var state = new OfflineModeState(source);

        Assert.Equal(1, source.Subscribers);
        _ = state.IsOffline;
        state.IsExpanded = true;
        state.IsExpanded = false;

        Assert.Equal(1, source.Subscribers);
    }

    [Fact]
    public void Dispose_stops_listening_and_can_be_called_twice()
    {
        var source = new FakeConnectivity();
        var state = new OfflineModeState(source);

        state.Dispose();
        state.Dispose();
        source.Raise(true);

        Assert.Equal(0, source.Subscribers);
        Assert.False(state.IsOffline);
    }
}
