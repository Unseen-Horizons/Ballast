// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth.Tests;

/// <summary>The sync status: what each kind of run records, what a snapshot carries, and how near the client secret is to its end.</summary>
public class MicrosoftSyncStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private static MicrosoftSyncCounts Counts() => new(2, 5, 1, 3, 4);

    private static MicrosoftSyncProblem Problem(string code = "NAME_MISSING", string subject = "ana@example.com", string? detail = null) =>
        new(code, subject, detail);

    private static MicrosoftSyncResult Run(
        MicrosoftSyncOutcome outcome,
        DirectorySyncFailure? failure = null,
        int? httpStatus = null,
        MicrosoftSyncCounts? counts = null,
        IReadOnlyList<MicrosoftSyncProblem>? problems = null,
        IReadOnlyList<string>? rolesNotInMicrosoft = null,
        DateTimeOffset? secretEndsUtc = null) =>
        new(outcome, failure, httpStatus, counts, problems ?? [], rolesNotInMicrosoft ?? [], secretEndsUtc);

    private static MicrosoftSyncResult Applied(DateTimeOffset? secretEndsUtc = null) =>
        Run(MicrosoftSyncOutcome.Applied, counts: Counts(), problems: [Problem()], rolesNotInMicrosoft: ["Retired"], secretEndsUtc: secretEndsUtc);

    [Fact]
    public void The_default_warning_window_is_thirty_days_and_a_negative_one_is_refused()
    {
        Assert.Equal(TimeSpan.FromDays(30), new MicrosoftSyncStatusOptions().SecretWarningWindow);
        new MicrosoftSyncStatusOptions { SecretWarningWindow = TimeSpan.Zero }.Validate();

        var error = Assert.Throws<InvalidOperationException>(() => new MicrosoftSyncStatusOptions { SecretWarningWindow = TimeSpan.FromDays(-1) }.Validate());
        Assert.Contains(nameof(MicrosoftSyncStatusOptions.SecretWarningWindow), error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => new MicrosoftSyncStatus(new MicrosoftSyncStatusOptions { SecretWarningWindow = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public void A_secret_with_no_end_date_is_unknown()
    {
        Assert.Equal(ClientSecretState.Unknown, new MicrosoftSyncStatus().SecretStateOf(null, Now));
    }

    [Fact]
    public void A_secret_that_has_ended_is_expired()
    {
        var status = new MicrosoftSyncStatus();

        Assert.Equal(ClientSecretState.Expired, status.SecretStateOf(Now.AddDays(-1), Now));
        Assert.Equal(ClientSecretState.Expired, status.SecretStateOf(Now, Now));
    }

    [Fact]
    public void A_secret_that_ends_within_the_window_is_soon_and_the_window_edge_counts()
    {
        var status = new MicrosoftSyncStatus();

        Assert.Equal(ClientSecretState.Soon, status.SecretStateOf(Now.AddDays(1), Now));
        Assert.Equal(ClientSecretState.Soon, status.SecretStateOf(Now.AddDays(30), Now));
    }

    [Fact]
    public void A_secret_that_ends_after_the_window_is_ok()
    {
        var status = new MicrosoftSyncStatus();

        Assert.Equal(ClientSecretState.Ok, status.SecretStateOf(Now.AddDays(30).AddSeconds(1), Now));
    }

    [Fact]
    public void The_warning_window_is_the_option()
    {
        var status = new MicrosoftSyncStatus(new MicrosoftSyncStatusOptions { SecretWarningWindow = TimeSpan.FromDays(7) });

        Assert.Equal(ClientSecretState.Ok, status.SecretStateOf(Now.AddDays(10), Now));
        Assert.Equal(ClientSecretState.Soon, status.SecretStateOf(Now.AddDays(7), Now));
    }

    [Fact]
    public void Before_any_run_the_snapshot_says_not_run_and_shows_no_figures()
    {
        var snapshot = new MicrosoftSyncStatus().Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.NotRun, snapshot.LastRun);
        Assert.Null(snapshot.LastAttemptUtc);
        Assert.Null(snapshot.LastSuccessUtc);
        Assert.Null(snapshot.Failure);
        Assert.Null(snapshot.FailureStatus);
        Assert.Equal(new MicrosoftSyncCounts(0, 0, 0, 0, 0), snapshot.Counts);
        Assert.Empty(snapshot.Problems);
        Assert.Empty(snapshot.RolesNotInMicrosoft);
        Assert.Null(snapshot.ClientSecretEndsUtc);
        Assert.Equal(ClientSecretState.Unknown, snapshot.ClientSecretState);
        Assert.False(snapshot.IsOff);
    }

    [Fact]
    public void An_applied_run_records_its_times_figures_problems_and_roles_not_in_Microsoft()
    {
        var status = new MicrosoftSyncStatus();

        status.Record(Applied(Now.AddDays(10)), Now);
        var snapshot = status.Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.Applied, snapshot.LastRun);
        Assert.Equal(Now, snapshot.LastAttemptUtc);
        Assert.Equal(Now, snapshot.LastSuccessUtc);
        Assert.Equal(Counts(), snapshot.Counts);
        Assert.Equal(4, snapshot.Counts.Created);
        Assert.Equal([Problem()], snapshot.Problems);
        Assert.Equal(["Retired"], snapshot.RolesNotInMicrosoft);
        Assert.Equal(Now.AddDays(10), snapshot.ClientSecretEndsUtc);
        Assert.Equal(ClientSecretState.Soon, snapshot.ClientSecretState);
    }

    [Fact]
    public void A_problem_keeps_its_detail()
    {
        var status = new MicrosoftSyncStatus();
        var problem = Problem("PROFILE_HOOK_FAILED", "ana@example.com", "InvalidOperationException");

        status.Record(Run(MicrosoftSyncOutcome.Applied, counts: Counts(), problems: [problem]), Now);

        Assert.Equal("InvalidOperationException", status.Snapshot(Now).Problems.Single().Detail);
    }

    [Fact]
    public void A_run_that_looks_wrong_shows_its_figures_and_roles_but_is_not_a_success()
    {
        var status = new MicrosoftSyncStatus();

        status.Record(Run(MicrosoftSyncOutcome.LooksWrong, counts: new MicrosoftSyncCounts(0, 0, 0, 3, 0), problems: [Problem()], rolesNotInMicrosoft: ["Retired"]), Now);
        var snapshot = status.Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.NotApplied, snapshot.LastRun);
        Assert.Equal(Now, snapshot.LastAttemptUtc);
        Assert.Null(snapshot.LastSuccessUtc);
        Assert.Equal(3, snapshot.Counts.SwitchedOff);
        Assert.Equal([Problem()], snapshot.Problems);
        Assert.Equal(["Retired"], snapshot.RolesNotInMicrosoft);
        Assert.True(status.IsNotInMicrosoft("Retired"));
    }

    [Fact]
    public void A_failed_read_records_its_kind_and_status_and_keeps_the_last_applied_figures()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(), Now.AddHours(-1));

        status.Record(Run(MicrosoftSyncOutcome.ReadFailed, DirectorySyncFailure.Graph, 503), Now);
        var snapshot = status.Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.Failed, snapshot.LastRun);
        Assert.Equal(DirectorySyncFailure.Graph, snapshot.Failure);
        Assert.Equal(503, snapshot.FailureStatus);
        Assert.Equal(Now, snapshot.LastAttemptUtc);
        Assert.Equal(Now.AddHours(-1), snapshot.LastSuccessUtc);
        Assert.Equal(Counts(), snapshot.Counts);
        Assert.Equal([Problem()], snapshot.Problems);
    }

    [Fact]
    public void A_failed_write_is_a_failure_and_keeps_the_figures()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(), Now.AddHours(-1));

        status.Record(Run(MicrosoftSyncOutcome.WriteFailed, DirectorySyncFailure.Unexpected), Now);
        var snapshot = status.Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.Failed, snapshot.LastRun);
        Assert.Equal(DirectorySyncFailure.Unexpected, snapshot.Failure);
        Assert.Null(snapshot.FailureStatus);
        Assert.Equal(Now.AddHours(-1), snapshot.LastSuccessUtc);
        Assert.Equal(["Retired"], snapshot.RolesNotInMicrosoft);
    }

    [Fact]
    public void A_failed_run_that_names_no_failure_is_refused()
    {
        var status = new MicrosoftSyncStatus();

        Assert.Throws<InvalidOperationException>(() => status.Record(Run(MicrosoftSyncOutcome.ReadFailed), Now));
    }

    [Fact]
    public void A_later_applied_run_clears_an_earlier_failure()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Run(MicrosoftSyncOutcome.ReadFailed, DirectorySyncFailure.Token, 401), Now.AddMinutes(-15));

        status.Record(Applied(), Now);
        var snapshot = status.Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.Applied, snapshot.LastRun);
        Assert.Null(snapshot.Failure);
        Assert.Null(snapshot.FailureStatus);
    }

    [Fact]
    public void A_run_on_a_copy_that_is_not_the_holder_records_nothing()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(), Now.AddHours(-1));

        status.Record(Run(MicrosoftSyncOutcome.NotHolder), Now);
        var snapshot = status.Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.Applied, snapshot.LastRun);
        Assert.Equal(Now.AddHours(-1), snapshot.LastAttemptUtc);
        Assert.Equal(Now.AddHours(-1), snapshot.LastSuccessUtc);
    }

    [Fact]
    public void Switching_the_sync_off_clears_the_figures_but_keeps_the_times_and_the_secret()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(Now.AddDays(10)), Now.AddHours(-1));

        status.RecordOff();
        var snapshot = status.Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.Off, snapshot.LastRun);
        Assert.True(snapshot.IsOff);
        Assert.Equal(new MicrosoftSyncCounts(0, 0, 0, 0, 0), snapshot.Counts);
        Assert.Empty(snapshot.Problems);
        Assert.Empty(snapshot.RolesNotInMicrosoft);
        Assert.False(status.IsNotInMicrosoft("Retired"));
        Assert.Equal(Now.AddHours(-1), snapshot.LastAttemptUtc);
        Assert.Equal(Now.AddHours(-1), snapshot.LastSuccessUtc);
        Assert.Equal(Now.AddDays(10), snapshot.ClientSecretEndsUtc);
    }

    [Fact]
    public void An_off_run_recorded_through_Record_is_the_same_as_RecordOff()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(), Now.AddHours(-1));

        status.Record(Run(MicrosoftSyncOutcome.Off), Now);

        Assert.True(status.Snapshot(Now).IsOff);
        Assert.Empty(status.Snapshot(Now).Problems);
    }

    [Fact]
    public void An_unexpected_failure_is_recorded_without_a_status_and_keeps_the_figures()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(), Now.AddHours(-1));

        status.RecordUnexpectedFailure(Now);
        var snapshot = status.Snapshot(Now);

        Assert.Equal(MicrosoftSyncLastRun.Failed, snapshot.LastRun);
        Assert.Equal(DirectorySyncFailure.Unexpected, snapshot.Failure);
        Assert.Null(snapshot.FailureStatus);
        Assert.Equal(Now, snapshot.LastAttemptUtc);
        Assert.Equal(Now.AddHours(-1), snapshot.LastSuccessUtc);
        Assert.Equal(["Retired"], snapshot.RolesNotInMicrosoft);
    }

    [Fact]
    public void A_run_with_no_secret_end_keeps_the_earlier_one()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(Now.AddDays(10)), Now.AddHours(-1));

        status.Record(Applied(), Now);

        Assert.Equal(Now.AddDays(10), status.Snapshot(Now).ClientSecretEndsUtc);
    }

    [Fact]
    public void A_run_with_a_secret_end_replaces_the_earlier_one_and_a_failure_can_carry_one()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(Now.AddDays(10)), Now.AddHours(-1));

        status.Record(Applied(Now.AddDays(90)), Now);
        Assert.Equal(Now.AddDays(90), status.Snapshot(Now).ClientSecretEndsUtc);

        status.Record(Run(MicrosoftSyncOutcome.ReadFailed, DirectorySyncFailure.Graph, 500, secretEndsUtc: Now.AddDays(91)), Now.AddMinutes(1));
        Assert.Equal(Now.AddDays(91), status.Snapshot(Now).ClientSecretEndsUtc);
    }

    [Fact]
    public void IsNotInMicrosoft_ignores_case_and_is_false_for_a_role_not_listed()
    {
        var status = new MicrosoftSyncStatus();
        Assert.False(status.IsNotInMicrosoft("Retired"));

        status.Record(Applied(), Now);

        Assert.True(status.IsNotInMicrosoft("retired"));
        Assert.True(status.IsNotInMicrosoft("RETIRED"));
        Assert.False(status.IsNotInMicrosoft("Editor"));
    }

    [Fact]
    public void IsNotInMicrosoft_refuses_a_null_role()
    {
        Assert.Throws<ArgumentNullException>(() => new MicrosoftSyncStatus().IsNotInMicrosoft(null!));
    }

    [Fact]
    public void Recording_a_null_run_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new MicrosoftSyncStatus().Record(null!, Now));
    }

    [Fact]
    public void Recording_an_outcome_the_status_does_not_know_is_refused()
    {
        var status = new MicrosoftSyncStatus();

        Assert.Throws<ArgumentOutOfRangeException>(() => status.Record(Run((MicrosoftSyncOutcome)99), Now));
    }

    [Fact]
    public void A_snapshot_is_a_copy_that_a_later_run_does_not_change()
    {
        var status = new MicrosoftSyncStatus();
        status.Record(Applied(), Now.AddHours(-1));
        var earlier = status.Snapshot(Now);

        status.Record(Run(MicrosoftSyncOutcome.Applied, counts: Counts(), problems: [Problem("GROUP_ASSIGNMENT", "g")], rolesNotInMicrosoft: []), Now);

        Assert.Equal([Problem()], earlier.Problems);
        Assert.Equal(["Retired"], earlier.RolesNotInMicrosoft);
        Assert.Equal([Problem("GROUP_ASSIGNMENT", "g")], status.Snapshot(Now).Problems);
        Assert.Empty(status.Snapshot(Now).RolesNotInMicrosoft);
    }

    [Fact]
    public void Snapshot_without_a_time_reads_the_clock_it_was_given()
    {
        var clock = new ManualTimeProvider { Now = Now };
        var status = new MicrosoftSyncStatus(time: clock);
        status.Record(Applied(Now.AddDays(10)), Now);

        Assert.Equal(ClientSecretState.Soon, status.Snapshot().ClientSecretState);

        clock.Now = Now.AddDays(11);

        Assert.Equal(ClientSecretState.Expired, status.Snapshot().ClientSecretState);
    }

    [Fact]
    public void Snapshot_with_a_time_judges_the_secret_at_that_time_not_the_clock()
    {
        var status = new MicrosoftSyncStatus(time: new ManualTimeProvider { Now = Now });
        status.Record(Applied(Now.AddDays(10)), Now);

        Assert.Equal(ClientSecretState.Expired, status.Snapshot(Now.AddDays(11)).ClientSecretState);
        Assert.Equal(ClientSecretState.Ok, status.Snapshot(Now.AddDays(-30)).ClientSecretState);
    }

    [Fact]
    public void The_gate_starts_free_for_a_single_run()
    {
        var status = new MicrosoftSyncStatus();

        Assert.Equal(1, status.Gate.CurrentCount);
    }

    [Fact]
    public void Concurrent_runs_and_reads_leave_a_consistent_status()
    {
        var status = new MicrosoftSyncStatus();

        Parallel.For(0, 400, i =>
        {
            if (i % 2 == 0)
            {
                status.Record(Applied(Now.AddDays(10)), Now.AddSeconds(i));
            }
            else
            {
                status.Record(Run(MicrosoftSyncOutcome.ReadFailed, DirectorySyncFailure.Timeout), Now.AddSeconds(i));
            }

            var snapshot = status.Snapshot(Now);
            Assert.NotNull(snapshot.LastAttemptUtc);
        });

        var final = status.Snapshot(Now);
        Assert.Contains(final.LastRun, new[] { MicrosoftSyncLastRun.Applied, MicrosoftSyncLastRun.Failed });
        Assert.NotNull(final.LastAttemptUtc);
    }
}
