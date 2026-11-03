// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

using Microsoft.Data.SqlClient;

namespace Ballast.Testing.Sql;

/// <summary>
/// Builds a <see cref="SqlException"/> with chosen error numbers. SqlClient has no public constructor for it (a real one only comes
/// from a real server), so this goes through the internal ones, the usual way to test code that reads error numbers. A test that
/// needs a real error uses the server instead.
/// </summary>
/// <remarks>
/// This depends on SqlClient's internal constructors. If a SqlClient release renames them, <see cref="Create"/> throws
/// <see cref="InvalidOperationException"/> with the name it could not find, so the break is loud rather than a wrong exception.
/// </remarks>
public static class SqlExceptions
{
    private const BindingFlags AnyAccess = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// A <see cref="SqlException"/> with one <see cref="SqlError"/> per number, in that order, so the first number is also
    /// <see cref="SqlException.Number"/>. Each error has state 1, class 16, a fixed server and procedure name, and line 1.
    /// </summary>
    /// <param name="numbers">The error numbers, in order. At least one is expected.</param>
    /// <exception cref="InvalidOperationException">This SqlClient version has no constructor the builder can use.</exception>
    public static SqlException Create(params int[] numbers)
    {
        var errorConstructor = typeof(SqlError).GetConstructor(
            AnyAccess, null, [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)], null)
            ?? throw new InvalidOperationException(
                "SqlError has no (number, state, class, server, message, procedure, line, exception) constructor in this version of SqlClient.");
        var collection = (SqlErrorCollection)typeof(SqlErrorCollection).GetConstructor(AnyAccess, null, Type.EmptyTypes, null)!.Invoke([]);
        var add = typeof(SqlErrorCollection).GetMethod("Add", AnyAccess, null, [typeof(SqlError)], null)!;

        foreach (var number in numbers)
        {
            add.Invoke(collection, [errorConstructor.Invoke([number, (byte)1, (byte)16, "test-server", $"Test SQL error {number}", "test-procedure", 1, null])]);
        }

        var create = typeof(SqlException).GetMethod(
            "CreateException", AnyAccess, null, [typeof(SqlErrorCollection), typeof(string), typeof(Guid), typeof(Exception)], null)
            ?? throw new InvalidOperationException(
                "SqlException has no CreateException(errors, serverVersion, connectionId, inner) in this version of SqlClient.");
        return (SqlException)create.Invoke(null, [collection, "16.0.0", Guid.NewGuid(), null])!;
    }
}
