// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Auth.Tests;

/// <summary>Records the SQL text of every reader command, so a test can see what EF sent to the server.</summary>
internal sealed class SqlRecorder : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    /// <summary>The SQL texts, in the order they ran.</summary>
    public IReadOnlyList<string> Commands => _commands;

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Add(command.CommandText);
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }
}

/// <summary>
/// The user key reaches the refresh-token query as a SQL parameter, never as a literal. A literal would make SQL Server compile one plan per
/// user, and EF cache one query per user.
/// </summary>
public class KeyParameterTests
{
    [Fact]
    public async Task A_string_user_key_is_sent_as_a_parameter()
    {
        var recorder = new SqlRecorder();
        await using var host = await Harness<StringUser, string, StringRefresh>.CreateAsync(interceptor: recorder);
        var userId = await host.CreateUserAsync("ana@example.test", "Correct-horse-42!");

        await using (var scope = host.Scope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<AuthSessionService<StringUser, string, StringRefresh, AuthTestDb<StringUser, string, StringRefresh>>>();
            await sessions.RevokeAllAsync(userId);
        }

        AssertKeyIsAParameter(recorder, userId);
    }

    [Fact]
    public async Task An_int_user_key_is_sent_as_a_parameter()
    {
        var recorder = new SqlRecorder();
        await using var host = await Harness<IntUser, int, IntRefresh>.CreateAsync(interceptor: recorder);
        var userId = await host.CreateUserAsync("ana@example.test", "Correct-horse-42!");

        await using (var scope = host.Scope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<AuthSessionService<IntUser, int, IntRefresh, AuthTestDb<IntUser, int, IntRefresh>>>();
            await sessions.RevokeAllAsync(userId);
        }

        AssertKeyIsAParameter(recorder, userId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The refresh-token query has a parameter, and the key's text is not written into it.</summary>
    private static void AssertKeyIsAParameter(SqlRecorder recorder, string keyText)
    {
        var sql = Assert.Single(recorder.Commands, text => text.Contains("RefreshTokens", StringComparison.Ordinal));
        Assert.Contains("@", sql, StringComparison.Ordinal);

        // The key must not appear as a literal. The pattern skips a match inside a parameter name, such as @__userId_0.
        Assert.DoesNotMatch(new Regex($@"(?<![\w@]){Regex.Escape(keyText)}(?!\w)"), sql);
    }
}
