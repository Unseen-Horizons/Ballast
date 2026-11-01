// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>The binary file types that <see cref="FileKinds.Detect"/> recognises.</summary>
public enum FileKind
{
    /// <summary>A JPEG image.</summary>
    Jpeg,

    /// <summary>A PNG image.</summary>
    Png,

    /// <summary>A WebP image.</summary>
    Webp,

    /// <summary>A PDF document.</summary>
    Pdf,

    /// <summary>A ZIP archive. Any ZIP archive has this signature, not only a particular kind of file that is packed as one.</summary>
    Zip,

    /// <summary>A HEIC or HEIF image (an ISO base media file with an image brand).</summary>
    Heic
}
