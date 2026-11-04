// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>What <see cref="AtomicFile"/> does when the file it is asked to write already exists.</summary>
public enum AtomicWriteMode
{
    /// <summary>
    /// The new content replaces the existing file. The replacement is one rename, so a reader sees the old file or the new
    /// one, never a mix of both.
    /// </summary>
    Overwrite,

    /// <summary>
    /// The write fails with an <see cref="IOException"/> when the file exists, and the existing file is left untouched. This
    /// is create-new: a name is taken once.
    /// </summary>
    NeverOverwrite
}
