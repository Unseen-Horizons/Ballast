// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

using Microsoft.Data.SqlClient;

namespace Ballast.SqlServer.Tests;

/// <summary>
/// Builds a <see cref="SqlException"/> with chosen error numbers. SqlClient has no public constructor for it (a real one only
/// comes from a real server), so this goes through the internal ones, the usual trick for testing code that reads error numbers.
/// A test that needs a real error uses the server instead (see <see cref="SqlErrorsSqlTests"/>).
/// </summary>
internal static class SqlExceptions
{
    private const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>One SqlError per number, in that order (so the first number is also <see cref="SqlException.Number"/>).</summary>
    public static SqlException Create(params int[] numbers)
    {
        var errorConstructor = typeof(SqlError).GetConstructor(
            NonPublic, null, [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)], null)
            ?? throw new InvalidOperationException("SqlError has no (number, state, class, server, message, procedure, line, exception) constructor in this version of SqlClient.");
        var collection = (SqlErrorCollection)typeof(SqlErrorCollection).GetConstructor(NonPublic, null, Type.EmptyTypes, null)!.Invoke([]);
        var add = typeof(SqlErrorCollection).GetMethod("Add", NonPublic, null, [typeof(SqlError)], null)!;

        foreach (var number in numbers)
        {
            add.Invoke(collection, [errorConstructor.Invoke([number, (byte)1, (byte)16, "test-server", $"Test SQL error {number}", "test-procedure", 1, null])]);
        }

        var create = typeof(SqlException).GetMethod(
            "CreateException", NonPublic, null, [typeof(SqlErrorCollection), typeof(string), typeof(Guid), typeof(Exception)], null)
            ?? throw new InvalidOperationException("SqlException has no CreateException(errors, serverVersion, connectionId, inner) in this version of SqlClient.");
        return (SqlException)create.Invoke(null, [collection, "16.0.0", Guid.NewGuid(), null])!;
    }
}
