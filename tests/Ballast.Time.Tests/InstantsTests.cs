// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Time.Tests;

public class InstantsTests
{
    [Fact]
    public void A_utc_value_is_returned_unchanged()
    {
        var value = new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Utc);

        Assert.Equal(value, Instants.AsUtc(value));
        Assert.Equal(DateTimeKind.Utc, Instants.AsUtc(value).Kind);
    }

    [Fact]
    public void An_unspecified_value_is_read_as_utc_without_moving_its_clock_reading()
    {
        var instant = Instants.AsUtc(new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Unspecified));

        Assert.Equal(new DateTime(2026, 9, 10, 12, 5, 0, DateTimeKind.Utc), instant);
        Assert.Equal(DateTimeKind.Utc, instant.Kind);
    }

    [Fact]
    public void A_local_value_is_converted_with_the_machines_own_zone_rules()
    {
        // The machine's zone decides the answer, so the expected value comes from the same conversion, not a fixed one.
        var local = new DateTime(2026, 9, 10, 14, 5, 0, DateTimeKind.Local);

        Assert.Equal(local.ToUniversalTime(), Instants.AsUtc(local));
        Assert.Equal(DateTimeKind.Utc, Instants.AsUtc(local).Kind);
    }
}
