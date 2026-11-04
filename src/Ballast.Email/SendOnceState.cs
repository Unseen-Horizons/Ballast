// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Email;

/// <summary>The state an app's store holds for one e-mail to one subject. The ledger writes these; the store keeps them.</summary>
public enum SendOnceState
{
    /// <summary>
    /// Reserved before the send. It stays Pending only while the send is running, or when the process died in the middle of it,
    /// so the mail may or may not have gone out.
    /// </summary>
    Pending,

    /// <summary>The provider accepted the e-mail (or it was written to the log in Development).</summary>
    Sent,

    /// <summary>The subject has no address, so nothing was sent. Final.</summary>
    NoAddress,

    /// <summary>The send did not deliver. It may be retried, under the ledger's options.</summary>
    Failed,
}
