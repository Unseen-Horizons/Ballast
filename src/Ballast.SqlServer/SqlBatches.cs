// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.RegularExpressions;

namespace Ballast.SqlServer;

/// <summary>
/// Splits a SQL script into the batches that SQL Server runs one at a time, on its <c>GO</c> lines. <c>GO</c> is a tool
/// separator, not T-SQL: a provider rejects it, so each batch is sent on its own.
/// </summary>
public static class SqlBatches
{
    // The rule of the sqlcmd and SSMS tools: a line that is only GO, in any case, with an optional ';'.
    private static readonly Regex BatchSeparator = new(@"^\s*GO\s*;?\s*$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Splits a script on its <c>GO</c> lines. A line counts as a separator only when it holds nothing but <c>GO</c> (any case, with
    /// optional white space and an optional <c>;</c>). The text between separators is kept as it was written, its line endings
    /// included. Batches that hold only white space are dropped.
    /// </summary>
    /// <param name="sql">The script. Required. May be empty.</param>
    /// <returns>The batches, in order. Empty when the script has no text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> is null.</exception>
    public static IReadOnlyList<string> Split(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        List<string> batches = [];
        var current = new StringBuilder();

        var lines = sql.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (BatchSeparator.IsMatch(lines[i]))
            {
                AddIfNotBlank(batches, current);
                current.Clear();
                continue;
            }

            current.Append(lines[i]);
            // Every line but the last one ended with a newline in the source.
            if (i < lines.Length - 1)
            {
                current.Append('\n');
            }
        }

        AddIfNotBlank(batches, current);
        return batches;
    }

    private static void AddIfNotBlank(List<string> batches, StringBuilder batch)
    {
        var text = batch.ToString();
        if (text.Trim().Length > 0)
        {
            batches.Add(text);
        }
    }
}
