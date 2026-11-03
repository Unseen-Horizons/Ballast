// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Sql;

namespace Ballast.Testing.Tests.Sql;

public class SqlExceptionsTests
{
    [Fact]
    public void The_first_number_is_the_exception_number_and_every_number_is_an_error()
    {
        var exception = SqlExceptions.Create(2601, 547, 1205);

        Assert.Equal(2601, exception.Number);
        Assert.Equal([2601, 547, 1205], exception.Errors.Cast<Microsoft.Data.SqlClient.SqlError>().Select(e => e.Number));
    }

    [Fact]
    public void Each_built_error_carries_its_number_in_its_message()
    {
        var exception = SqlExceptions.Create(4060);

        var error = Assert.Single(exception.Errors.Cast<Microsoft.Data.SqlClient.SqlError>());
        Assert.Equal("Test SQL error 4060", error.Message);
        Assert.Equal((byte)16, error.Class);
    }
}
