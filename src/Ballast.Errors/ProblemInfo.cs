// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Errors;

/// <summary>
/// The fields of a problem answer that a client reads. A field is null when the answer does not carry it.
/// </summary>
/// <param name="Code">The error code, for the client to translate.</param>
/// <param name="Title">The server's title, for when the client has no translation for the code.</param>
/// <param name="Detail">The developer sentence about the occurrence.</param>
/// <param name="TraceId">The trace reference that joins the client's log to the server's.</param>
public sealed record ProblemInfo(string? Code, string? Title, string? Detail = null, string? TraceId = null);
