// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Statistics;

namespace Ballast.Statistics.Tests;

public class StatsTests
{
    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected} but was {actual}");

    [Fact]
    public void NormalUpperTail_matches_published_values()
    {
        AssertClose(0.5, Stats.NormalUpperTail(0), 1e-12);
        AssertClose(0.0249979, Stats.NormalUpperTail(1.96), 1e-7);
        AssertClose(0.0013499, Stats.NormalUpperTail(3.0), 1e-7);
        AssertClose(0.9750021, Stats.NormalUpperTail(-1.96), 1e-7);
    }

    [Fact]
    public void NormalUpperTail_is_accurate_across_the_series_and_the_continued_fraction()
    {
        // Published normal tail values: Q(0.5) = 0.3085375387 (table), Q(4.2) = 1.33457e-5 (table, 1.3345 × 10^-5).
        AssertClose(0.3085375387, Stats.NormalUpperTail(0.5), 1e-9);
        AssertClose(0.0000133457, Stats.NormalUpperTail(4.2), 1e-8);
        Assert.True(Stats.NormalUpperTail(40) >= 0);
        Assert.True(Stats.NormalUpperTail(40) < 1e-300);
    }

    [Fact]
    public void NormalUpperTail_at_infinities_gives_zero_and_one()
    {
        Assert.Equal(0.0, Stats.NormalUpperTail(double.PositiveInfinity));
        Assert.Equal(1.0, Stats.NormalUpperTail(double.NegativeInfinity));
    }

    [Fact]
    public void NormalUpperTail_names_z_when_it_is_nan()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Stats.NormalUpperTail(double.NaN));
        Assert.Equal("z", ex.ParamName);
    }

    [Fact]
    public void TwoProportionZ_matches_the_hand_worked_example()
    {
        // Pooled share 50 / 200 = 0.25. Standard error sqrt(0.25 × 0.75 × (1/100 + 1/100)) = sqrt(0.00375) = 0.0612372.
        // (0.30 − 0.20) / 0.0612372 = 1.632993.
        var z = Stats.TwoProportionZ(30, 100, 20, 100);
        Assert.NotNull(z);
        AssertClose(1.632993, z!.Value, 1e-6);
    }

    [Fact]
    public void TwoProportionZ_is_negative_when_the_first_share_is_lower()
    {
        var z = Stats.TwoProportionZ(20, 100, 30, 100);
        Assert.NotNull(z);
        AssertClose(-1.632993, z!.Value, 1e-6);
    }

    [Fact]
    public void TwoProportionZ_is_null_when_the_pooled_share_is_0_or_1()
    {
        Assert.Null(Stats.TwoProportionZ(0, 10, 0, 10));
        Assert.Null(Stats.TwoProportionZ(10, 10, 20, 20));
    }

    [Fact]
    public void TwoProportionZ_is_null_when_a_group_has_no_trials()
    {
        Assert.Null(Stats.TwoProportionZ(0, 0, 5, 10));
        Assert.Null(Stats.TwoProportionZ(3, 10, 0, 0));
    }

    [Fact]
    public void TwoProportionZ_names_a_count_that_is_negative_or_more_than_its_trials()
    {
        var negative = Assert.Throws<ArgumentOutOfRangeException>(() => Stats.TwoProportionZ(-1, 10, 2, 10));
        Assert.Equal("seen1", negative.ParamName);

        var more = Assert.Throws<ArgumentOutOfRangeException>(() => Stats.TwoProportionZ(3, 10, 11, 10));
        Assert.Equal("seen2", more.ParamName);

        var trials = Assert.Throws<ArgumentOutOfRangeException>(() => Stats.TwoProportionZ(0, -1, 0, 10));
        Assert.Equal("n1", trials.ParamName);
    }

    [Fact]
    public void BenjaminiHochberg_matches_the_worked_example()
    {
        // Sorted: 0.005 (rank 1), 0.01 (rank 2), 0.03 (rank 3), 0.04 (rank 4). Raw m/rank × p: 0.02, 0.02, 0.04, 0.04.
        // Running minimum from the top: 0.04, 0.04, 0.02, 0.02. In input order: 0.02, 0.04, 0.04, 0.02.
        var adjusted = Stats.BenjaminiHochberg([0.01, 0.04, 0.03, 0.005], 0.05);
        Assert.Equal(4, adjusted.Count);
        AssertClose(0.02, adjusted[0], 1e-12);
        AssertClose(0.04, adjusted[1], 1e-12);
        AssertClose(0.04, adjusted[2], 1e-12);
        AssertClose(0.02, adjusted[3], 1e-12);
    }

    [Fact]
    public void BenjaminiHochberg_is_capped_at_one()
    {
        // Raw values: 0.9 × 3 = 2.7, 0.95 × 3 / 2 = 1.425, 1.0 × 3 / 3 = 1.0. Every adjusted value is capped at 1.
        var adjusted = Stats.BenjaminiHochberg([0.9, 0.95, 1.0], 0.05);
        foreach (var p in adjusted) AssertClose(1.0, p, 1e-12);
    }

    [Fact]
    public void BenjaminiHochberg_is_monotone_in_the_p_values_and_never_below_them()
    {
        double[] p = [0.001, 0.2, 0.0004, 0.03, 0.5, 0.049, 0.8];
        var adjusted = Stats.BenjaminiHochberg(p, 0.1);
        for (var i = 0; i < p.Length; i++)
        {
            Assert.True(adjusted[i] >= p[i] - 1e-12, $"adjusted {adjusted[i]} is below p {p[i]}");
            Assert.True(adjusted[i] <= 1.0);
        }
        for (var i = 0; i < p.Length; i++)
        for (var j = 0; j < p.Length; j++)
            if (p[i] <= p[j]) Assert.True(adjusted[i] <= adjusted[j] + 1e-12, $"p {p[i]} gave {adjusted[i]}, above {adjusted[j]} of p {p[j]}");
    }

    [Fact]
    public void BenjaminiHochberg_of_no_p_values_is_empty()
    {
        Assert.Empty(Stats.BenjaminiHochberg([], 0.05));
    }

    [Fact]
    public void BenjaminiHochberg_names_q_when_it_is_not_between_0_and_1()
    {
        Assert.Equal("q", Assert.Throws<ArgumentOutOfRangeException>(() => Stats.BenjaminiHochberg([0.5], 0)).ParamName);
        Assert.Equal("q", Assert.Throws<ArgumentOutOfRangeException>(() => Stats.BenjaminiHochberg([0.5], 1)).ParamName);
        Assert.Equal("q", Assert.Throws<ArgumentOutOfRangeException>(() => Stats.BenjaminiHochberg([0.5], double.NaN)).ParamName);
    }

    [Fact]
    public void BenjaminiHochberg_names_pValues_when_one_is_outside_0_to_1()
    {
        Assert.Equal("pValues", Assert.Throws<ArgumentOutOfRangeException>(() => Stats.BenjaminiHochberg([0.2, 1.5], 0.05)).ParamName);
        Assert.Equal("pValues", Assert.Throws<ArgumentOutOfRangeException>(() => Stats.BenjaminiHochberg([-0.1], 0.05)).ParamName);
        Assert.Equal("pValues", Assert.Throws<ArgumentOutOfRangeException>(() => Stats.BenjaminiHochberg([double.NaN], 0.05)).ParamName);
    }

    [Fact]
    public void Median_of_an_odd_count_is_the_middle_value()
    {
        AssertClose(2.0, Stats.Median([3.0, 1.0, 2.0]), 1e-12);
    }

    [Fact]
    public void Median_of_an_even_count_is_the_mean_of_the_two_middle_values()
    {
        AssertClose(2.5, Stats.Median([1.0, 2.0, 3.0, 4.0]), 1e-12);
    }

    [Fact]
    public void Mad_of_an_odd_count_is_the_raw_median_absolute_deviation()
    {
        // Median 3. Deviations 2, 1, 0, 1, 97; their median is 1. No scale factor.
        AssertClose(1.0, Stats.Mad([1.0, 2.0, 3.0, 4.0, 100.0]), 1e-12);
    }

    [Fact]
    public void Mad_of_an_even_count_is_the_raw_median_absolute_deviation()
    {
        // Median 2.5. Deviations 1.5, 0.5, 0.5, 1.5; their median is 1.0.
        AssertClose(1.0, Stats.Mad([1.0, 2.0, 3.0, 4.0]), 1e-12);
    }

    [Fact]
    public void Mad_is_zero_when_the_values_are_equal()
    {
        AssertClose(0.0, Stats.Mad([5.0, 5.0, 5.0]), 1e-12);
    }

    [Fact]
    public void Median_and_Mad_of_an_empty_list_throw()
    {
        Assert.Throws<ArgumentException>(() => Stats.Median([]));
        Assert.Throws<ArgumentException>(() => Stats.Mad([]));
    }

    [Fact]
    public void Median_names_values_when_a_value_is_not_finite()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Stats.Median([1.0, double.NaN]));
        Assert.Equal("values", ex.ParamName);
        Assert.Equal("values", Assert.Throws<ArgumentOutOfRangeException>(() => Stats.Mad([1.0, double.PositiveInfinity])).ParamName);
    }
}
