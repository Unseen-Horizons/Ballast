// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http;

/// <summary>
/// A response body that is larger than the cap the caller set, either declared as such or read past the cap. Nothing is
/// returned for it: a cap is reported, never cut silently. The caller maps it to its own error.
/// </summary>
public sealed class BodyTooLargeException : Exception
{
    /// <summary>Creates the exception for a body over <paramref name="maxBytes"/>.</summary>
    /// <param name="maxBytes">The cap that was exceeded.</param>
    public BodyTooLargeException(long maxBytes)
        : base($"Response exceeds the {maxBytes} byte limit.")
    {
        MaxBytes = maxBytes;
    }

    /// <summary>The cap that was exceeded, in bytes.</summary>
    public long MaxBytes { get; }
}
