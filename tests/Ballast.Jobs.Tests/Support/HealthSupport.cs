// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs.Tests;

/// <summary>A clock the test moves by hand. It starts at a fixed instant, so every assertion about a window is exact.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>A leadership with every value the job-lock readiness check reads, set by the test.</summary>
internal sealed class FakeLeadership : IJobLeadership
{
    public bool Enabled { get; set; } = true;

    public bool IsHolder { get; set; }

    public DateTime? HolderSinceUtc { get; set; }

    public DateTime? LastAttemptUtc { get; set; }

    public JobLeadershipOutcome LastOutcome { get; set; } = JobLeadershipOutcome.NotStarted;

    public string? LastError { get; set; }

    public DateTime StartedUtc { get; set; }

    public Task<CancellationToken> WaitForLeadershipAsync(CancellationToken ct) => throw new NotSupportedException("Not used by the readiness check.");

    public Task RunAsLeaderAsync(Func<CancellationToken, Task> work, bool once, CancellationToken ct) => throw new NotSupportedException("Not used by the readiness check.");
}
