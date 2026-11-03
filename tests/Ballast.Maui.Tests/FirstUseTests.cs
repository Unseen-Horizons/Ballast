// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Helpers;

namespace Ballast.Maui.Tests;

/// <summary>The gate behind a lazily built section: built the first time it is wanted, kept after that, and never built for a section nobody asked for.</summary>
public sealed class FirstUseTests
{
    [Fact]
    public void Nothing_is_taken_before_it_is_wanted()
    {
        var gate = new FirstUse();

        Assert.False(gate.Take(false));
        Assert.False(gate.Take(false));
        Assert.False(gate.IsTaken);
    }

    [Fact]
    public void The_first_wanted_call_says_yes_and_later_ones_say_no()
    {
        var gate = new FirstUse();

        Assert.True(gate.Take(true));
        Assert.True(gate.IsTaken);
        Assert.False(gate.Take(true));
        Assert.False(gate.Take(true));
    }

    [Fact]
    public void Hiding_and_showing_again_does_not_build_a_second_time()
    {
        var gate = new FirstUse();

        Assert.False(gate.Take(false));
        Assert.True(gate.Take(true));
        Assert.False(gate.Take(false));
        Assert.False(gate.Take(true));
    }

    [Fact]
    public void Each_gate_is_independent()
    {
        var first = new FirstUse();
        var second = new FirstUse();

        Assert.True(first.Take(true));
        Assert.False(second.IsTaken);
        Assert.True(second.Take(true));
    }
}
