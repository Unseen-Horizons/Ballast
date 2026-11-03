// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The one line a test assembly writes to use the SQL test databases: the fixture sweeps the leftovers and drops this run's own.
[assembly: AssemblyFixture(typeof(Ballast.Testing.Sql.TestDatabaseRun))]
