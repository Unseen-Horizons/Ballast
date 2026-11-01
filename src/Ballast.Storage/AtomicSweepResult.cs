// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>What <see cref="AtomicFile.SweepStale"/> removed, and what it found old but could not remove.</summary>
/// <param name="Removed">The full paths of the temporary files that were removed. Never null, and empty when there were none.</param>
/// <param name="Failed">The temporary files that were old but could not be removed, each with its error. Never null, and empty when there were none.</param>
public sealed record AtomicSweepResult(IReadOnlyList<string> Removed, IReadOnlyList<AtomicSweepFailure> Failed);

/// <summary>A temporary file that <see cref="AtomicFile.SweepStale"/> found old and could not remove.</summary>
/// <param name="FullPath">The full path of the file.</param>
/// <param name="Error">The error the removal raised.</param>
public sealed record AtomicSweepFailure(string FullPath, Exception Error);
