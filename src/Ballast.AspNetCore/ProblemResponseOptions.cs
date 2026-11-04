// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;

namespace Ballast.AspNetCore;

/// <summary>
/// The options of the problem answers. They are set once, in <see cref="ProblemServiceExtensions.AddProblemResponses"/>, and
/// read for the life of the application.
/// </summary>
public sealed class ProblemResponseOptions
{
    /// <summary>
    /// The rules that turn an exception that is not an <see cref="Ballast.Errors.AppException"/> into an answer, in the order they
    /// are checked. The first rule that matches answers the request. Empty by default, so every such exception is answered with
    /// the internal error. Each rule is checked when the options are added, and a rule that is not valid is refused there.
    /// </summary>
    public IList<ExceptionMapping> ExceptionMappings { get; } = [];

    /// <summary>
    /// The detail of the 404 answer for an unknown route under a prefix, from <see cref="ProblemEndpointExtensions.MapApiNotFound"/>.
    /// It is a function of the request, so a localised text can be written. The default is "No endpoint is at this address.".
    /// </summary>
    public Func<HttpContext, string> EndpointNotFoundDetail { get; set; } = static _ => "No endpoint is at this address.";
}
