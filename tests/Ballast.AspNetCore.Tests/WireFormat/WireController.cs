// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel.DataAnnotations;

using System.Diagnostics.CodeAnalysis;

using Ballast.Errors;

using Microsoft.AspNetCore.Mvc;

namespace Ballast.AspNetCore.Tests.WireFormat;

/// <summary>The request body of the validation test: a required name.</summary>
public sealed class WireBody
{
    /// <summary>The name, required.</summary>
    [Required]
    public string? Name { get; set; }
}

/// <summary>The endpoints the wire tests call. Each one produces one of the answers the library writes.</summary>
[SuppressMessage("Performance", "CA1822", Justification = "Controller actions are instance members, as the framework requires.")]
[ApiController]
[Route("wire")]
public sealed class WireController : ControllerBase
{
    /// <summary>A body that fails model validation, which the framework answers.</summary>
    [HttpPost("validate")]
    public IActionResult Validate([FromBody] WireBody body) => Ok();

    /// <summary>A refusal with an extension member.</summary>
    [HttpGet("refused")]
    public IActionResult Refused() =>
        throw new AppException("ITEM_LOCKED", "The item is locked.", 409, "Detail text.", new Dictionary<string, object?> { ["field"] = "X" });

    /// <summary>An unexpected failure, whose text must not reach the client.</summary>
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret internal state");

    /// <summary>A successful answer.</summary>
    [HttpGet("ok")]
    public IActionResult Ok2() => Ok(new { a = 1 });
}
