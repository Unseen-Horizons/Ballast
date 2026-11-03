// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// Holds one value for a query expression. EF Core sends a value read from a constant holder as a SQL parameter, where a value
/// written into the expression as a constant would be sent as a literal, and each literal makes SQL Server compile its own plan.
/// </summary>
/// <typeparam name="T">The value's type.</typeparam>
internal sealed class QueryValue<T>
{
    /// <summary>Creates the holder.</summary>
    /// <param name="value">The value the query compares with.</param>
    public QueryValue(T value)
    {
        Value = value;
    }

    /// <summary>The value. A field, so the expression can read it directly.</summary>
    internal readonly T Value;
}
