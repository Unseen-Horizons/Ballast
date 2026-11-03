// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ballast.Testing.Tests.Guards;

// The endpoint-authorization rules read this test assembly's controllers, so these fixtures carry the authorization shapes the resolver must read.

/// <summary>The fake app-session marker. The tests pass its name as the marker option.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class EpMarkerAttribute : Attribute
{
}

/// <summary>A fake controller that is signed in by default, with one policy action, one anonymous action and one non-action.</summary>
[Authorize]
[Route("[controller]")]
public sealed class EpSecureController : ControllerBase
{
    [HttpGet("orders/{id:int}")]
    public IActionResult Get(int id) => Ok();

    [HttpGet("admin"), Authorize(Policy = "orders.manage")]
    public IActionResult Admin() => Ok();

    [HttpGet("open"), AllowAnonymous]
    public IActionResult Open() => Ok();

    [NonAction]
    public IActionResult Helper() => Ok();
}

/// <summary>A fake controller with no authorization attribute at all: the resolver must report it as unprotected.</summary>
[Route("[controller]")]
public sealed class EpNoneController : ControllerBase
{
    [HttpGet("nothing")]
    public IActionResult Nothing() => Ok();
}

/// <summary>A fake controller with the app-session marker: one action without anonymous access, one with it.</summary>
[Authorize]
[Route("[controller]")]
public sealed class EpMarkedController : ControllerBase
{
    [HttpGet("blocked"), EpMarker]
    public IActionResult Blocked() => Ok();

    [HttpGet("data"), AllowAnonymous, EpMarker]
    public IActionResult Data() => Ok();
}
