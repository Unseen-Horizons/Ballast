// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// Where the sign-in library writes its audit rows (a login, a refresh, a reuse, a password change). The app implements it over its
/// own audit table. <see cref="Add"/> only adds the row to the app's database context: the library saves it with the change it
/// records, so a row is never written without its change, or the other way round.
/// </summary>
/// <remarks>
/// The app must register an implementation. Without one, the first audited action fails loudly with a missing-service error, which is
/// the intent: the library does not drop an audit row quietly.
/// </remarks>
public interface IAuthAudit
{
    /// <summary>Adds one audit row to the app's context, to be saved with the change it records.</summary>
    /// <param name="entry">The row to add.</param>
    void Add(AuthAuditEntry entry);
}
