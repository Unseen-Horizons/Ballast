// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AspNetCore;

/// <summary>The settings of a <see cref="SecretProtector"/>.</summary>
public sealed class SecretProtectorOptions
{
    /// <summary>
    /// The data-protection purpose. It scopes the key: a payload protected for one purpose cannot be opened by a protector
    /// with another, even over the same key ring. An application keeps its exact value, because changing it makes every
    /// stored credential unreadable. Required; a blank value stops the start.
    /// </summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>What <see cref="SecretProtector.Unprotect"/> does with an unreadable credential. Default <see cref="SecretUnreadableMode.Throw"/>.</summary>
    public SecretUnreadableMode UnreadableMode { get; set; } = SecretUnreadableMode.Throw;
}
