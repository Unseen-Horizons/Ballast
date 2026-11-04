// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AspNetCore;

/// <summary>What <see cref="SecretProtector.Unprotect"/> does with a stored credential that cannot be decrypted.</summary>
public enum SecretUnreadableMode
{
    /// <summary>
    /// <see cref="SecretProtector.Unprotect"/> throws <see cref="SecretProtector.CredentialUnavailableException"/>. The caller
    /// fails the read, so a request is never sent without its credential. The default.
    /// </summary>
    Throw = 0,

    /// <summary>
    /// <see cref="SecretProtector.Unprotect"/> returns null, which reads as "no credential". The caller must treat null as a
    /// reason to refuse or to warn, because a request sent on null goes out unauthenticated.
    /// </summary>
    ReadAsNull = 1,
}
