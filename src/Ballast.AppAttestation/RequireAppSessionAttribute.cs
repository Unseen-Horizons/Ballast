// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppAttestation;

/// <summary>
/// Marks an endpoint as a data call that only an app session or a signed-in user may make. <see cref="AppSessionGateMiddleware"/>
/// enforces it, or, while <see cref="AppAttestationOptions.Enforce"/> is off, only logs what it would have refused.
/// <para>
/// A marker, not an authorization policy, on purpose. These actions sit on controllers that carry a class-level <c>[Authorize]</c>,
/// and ASP.NET applies every <c>[Authorize]</c> in the chain together (all of them must pass), so a policy on the action would still
/// demand a user token and refuse the app. The action keeps its <c>[AllowAnonymous]</c> (no framework challenge), and this attribute
/// is the access rule. An app's surface test reads it by its name, so the name is part of the contract.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAppSessionAttribute : Attribute;
