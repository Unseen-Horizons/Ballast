// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;

// The sweep primitives (the leftover names of each run, and the drop of a run name) are internal: an app reaches them through
// TestDatabaseRun. Only this library's own tests see them.
[assembly: InternalsVisibleTo("Ballast.Testing.Tests")]
