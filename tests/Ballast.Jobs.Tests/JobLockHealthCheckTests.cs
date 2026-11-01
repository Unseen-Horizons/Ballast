// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ballast.Jobs.Tests;

/// <summary>
/// The job-lock check. The rule that matters most: a copy is never required to hold the lock, because the old copy only stops
/// after the new one is ready. Every state in which the copy is a sensible part of the lock is Healthy.
/// </summary>
public class JobLockHealthCheckTests
{
    private static (JobLockHealthCheck Check, FakeLeadership Leadership, ManualTime Time) Make(bool registered = true, int graceMinutes = 15)
    {
        var time = new ManualTime();
        var leadership = new FakeLeadership { StartedUtc = time.GetUtcNow().UtcDateTime };
        var options = Options.Create(new JobLockCheckOptions { JobLockGraceMinutes = graceMinutes });
        var check = new JobLockHealthCheck(options, time, registered ? leadership : null);
        return (check, leadership, time);
    }

    private static async Task<HealthStatus> StatusAsync(JobLockHealthCheck check) =>
        (await check.CheckHealthAsync(new HealthCheckContext())).Status;

    [Fact]
    public async Task A_switched_off_lock_is_healthy()
    {
        var (check, leadership, time) = Make();
        leadership.Enabled = false;
        leadership.LastOutcome = JobLeadershipOutcome.Disabled;
        time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));
    }

    [Fact]
    public async Task A_copy_that_holds_the_lock_is_healthy()
    {
        var (check, leadership, _) = Make();
        leadership.IsHolder = true;
        leadership.LastOutcome = JobLeadershipOutcome.Acquired;

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));
    }

    [Fact]
    public async Task Held_by_another_copy_for_two_hours_is_healthy()
    {
        // The deploy's new copy waits behind the old one for as long as the old one runs. That is not a fault.
        var (check, leadership, time) = Make();
        leadership.IsHolder = false;
        leadership.LastOutcome = JobLeadershipOutcome.HeldByOther;
        time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));
    }

    [Fact]
    public async Task Acquired_while_the_term_start_hooks_run_is_healthy_even_before_it_is_the_holder()
    {
        // The import recovery can take minutes while IsHolder is still false. Reporting that as bad would let a long recovery
        // fail the copy.
        var (check, leadership, time) = Make();
        leadership.IsHolder = false;
        leadership.LastOutcome = JobLeadershipOutcome.Acquired;
        time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));
    }

    [Fact]
    public async Task Released_while_the_copy_is_stopping_is_healthy()
    {
        var (check, leadership, time) = Make();
        leadership.IsHolder = false;
        leadership.LastOutcome = JobLeadershipOutcome.Released;
        time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));
    }

    [Theory]
    [InlineData(JobLeadershipOutcome.Error)]
    [InlineData(JobLeadershipOutcome.NotStarted)]
    public async Task A_bad_streak_is_not_unhealthy_for_14_minutes_but_is_at_15(JobLeadershipOutcome outcome)
    {
        var (check, leadership, time) = Make();
        leadership.IsHolder = false;
        leadership.LastOutcome = outcome;

        time.Advance(TimeSpan.FromMinutes(14));
        Assert.NotEqual(HealthStatus.Unhealthy, await StatusAsync(check));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HealthStatus.Unhealthy, await StatusAsync(check));
    }

    [Fact]
    public async Task The_grace_is_the_option_the_check_is_given()
    {
        // With a grace of three minutes, the copy is Unhealthy at three, where the default would still be Healthy.
        var (check, leadership, time) = Make(graceMinutes: 3);
        leadership.LastOutcome = JobLeadershipOutcome.Error;

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HealthStatus.Unhealthy, await StatusAsync(check));
    }

    [Fact]
    public async Task A_bad_streak_is_degraded_from_five_minutes()
    {
        var (check, leadership, time) = Make();
        leadership.LastOutcome = JobLeadershipOutcome.Error;

        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HealthStatus.Degraded, await StatusAsync(check));
    }

    [Fact]
    public async Task A_good_answer_ends_the_streak_and_the_next_bad_one_starts_a_new_one()
    {
        var (check, leadership, time) = Make();
        leadership.LastOutcome = JobLeadershipOutcome.Error;
        time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HealthStatus.Degraded, await StatusAsync(check));

        leadership.LastOutcome = JobLeadershipOutcome.Acquired;
        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));

        leadership.LastOutcome = JobLeadershipOutcome.Error;
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));
    }

    [Fact]
    public async Task No_job_lock_registered_is_healthy()
    {
        var (check, _, time) = Make(registered: false);
        time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HealthStatus.Healthy, await StatusAsync(check));
    }
}
