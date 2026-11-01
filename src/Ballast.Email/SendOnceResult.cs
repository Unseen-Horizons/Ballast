// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>What one call to <see cref="SendOnceLedger.SendOnceAsync"/> did, and the provider's answer when it sent.</summary>
/// <param name="Outcome">What the call did.</param>
/// <param name="Delivery">
/// The result of the send, when this call sent the e-mail (<see cref="SendOnceOutcome.Sent"/> or <see cref="SendOnceOutcome.Failed"/>).
/// Null when nothing was sent.
/// </param>
public sealed record SendOnceResult(SendOnceOutcome Outcome, EmailSendResult? Delivery = null);
