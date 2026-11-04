// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Ballast.Testing.Tests.Guards;

// The job-holder rules read this test assembly's hosted services and controllers, so these fixtures are its only ones of each kind.

/// <summary>The fake holder marker. Stands in for the app's attribute that answers an admin action only in the job holder.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class FakeHolderAttribute : Attribute
{
}

/// <summary>The fake job-leadership interface. A worker that takes it can tell the holder from the copies.</summary>
public interface IFakeLeadership
{
}

/// <summary>A fake worker that writes behind the lock.</summary>
public sealed class FakeHolderWorker : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A fake worker that runs in every copy.</summary>
public sealed class FakeEveryCopyWorker : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A fake worker split between the holder and every copy. It takes the leadership, so the rule accepts it.</summary>
public sealed class FakeMixedWorker : IHostedService
{
    public FakeMixedWorker(IFakeLeadership leadership)
    {
        Leadership = leadership;
    }

    public IFakeLeadership Leadership { get; }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A fake manager the mixed worker's leadership is carried by: its constructor takes the leadership.</summary>
public sealed class FakeLeadershipCarrier
{
    public FakeLeadershipCarrier(IFakeLeadership leadership)
    {
        Leadership = leadership;
    }

    public IFakeLeadership Leadership { get; }
}

/// <summary>A fake carrier whose constructor does not take the leadership, so the rule refuses it.</summary>
public sealed class FakeNoLeadershipCarrier
{
}

/// <summary>A fake controller with holder-only actions, one of them a publish-anyway action.</summary>
[Route("fake-jobs")]
public sealed class FakeJobsController : ControllerBase
{
    [HttpPost("start"), FakeHolder]
    public IActionResult Start() => Ok();

    [HttpPost("publish-anyway"), FakeHolder]
    public IActionResult PublishAnyway() => Ok();

    [HttpGet("status")]
    public IActionResult Status() => Ok();
}

/// <summary>A fake controller whose every action is holder-only, marked on the class.</summary>
[FakeHolder]
[Route("fake-all")]
public sealed class FakeAllHolderController : ControllerBase
{
    [HttpPost("one")]
    public IActionResult One() => Ok();
}

/// <summary>A fake publish action with no holder attribute. The rule must flag it.</summary>
[Route("fake-unheld")]
public sealed class FakeUnheldController : ControllerBase
{
    [HttpPost("probe-publish")]
    public IActionResult Probe() => Ok();
}
