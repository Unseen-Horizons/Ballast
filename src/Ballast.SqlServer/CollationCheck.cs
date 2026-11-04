// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ballast.SqlServer;

/// <summary>
/// Asserts that a database compares text case-insensitively. An app that matches identifiers with a case-insensitive comparer
/// (<c>StringComparer.OrdinalIgnoreCase</c>) relies on it: on a case-sensitive database, two identifiers that differ only by case
/// stop matching, and nothing says why. The database collation is whatever the server was created with, and a restore onto a
/// server with another default is a realistic way to get there, so the requirement is checked rather than assumed.
/// </summary>
public static class CollationCheck
{
    /// <summary>
    /// The probe. It compares two literals under the database's default collation. Its one column is <c>Value</c>.
    /// </summary>
    public const string Query = "SELECT CASE WHEN N'a' = N'A' THEN 1 ELSE 0 END AS [Value]";

    /// <summary>The message <see cref="EnsureCaseInsensitiveAsync"/> throws when the app gives none.</summary>
    public const string DefaultFailureMessage =
        "The database compares text case-sensitively, but identifiers are matched case-insensitively. "
        + "Ids that differ only by case would silently stop matching. Use a case-insensitive collation, "
        + "such as SQL_Latin1_General_CP1_CI_AS or Latin1_General_100_CI_AS.";

    /// <summary>
    /// Reads the answer of <see cref="Query"/>, which is 1 for a case-insensitive database. Anything else, including no answer, is
    /// <see cref="CollationResult.CaseSensitive"/>.
    /// </summary>
    /// <param name="queryResult">The answer of <see cref="Query"/>, or null when it gave no row.</param>
    /// <returns>The classification of the answer.</returns>
    public static CollationResult Evaluate(int? queryResult) =>
        queryResult == 1 ? CollationResult.CaseInsensitive : CollationResult.CaseSensitive;

    /// <summary>
    /// Runs the probe on the context's database and classifies the answer. Nothing is thrown for a probe that cannot run: the
    /// result is <see cref="CollationResult.Unverified"/>, and the failure is logged when a logger is given.
    /// </summary>
    /// <param name="db">The context whose database is checked. Required.</param>
    /// <param name="logger">Logs a probe that could not run. Null logs nothing.</param>
    /// <param name="ct">Cancels the probe. A cancellation is thrown, not classified.</param>
    /// <returns>The classification.</returns>
    /// <remarks>
    /// A context with no provider configured is a configuration error, and EF Core's <c>InvalidOperationException</c> is thrown
    /// for it. It is not classified.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="db"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The context has no database provider configured (EF Core's message).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public static async Task<CollationResult> CheckAsync(DbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!db.Database.IsRelational())
        {
            return CollationResult.NotRelational;
        }

        try
        {
            var rows = await db.Database.SqlQueryRaw<int>(Query).ToListAsync(ct);
            return Evaluate(rows.Count == 1 ? rows[0] : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Could not verify the database collation; the check was skipped.");
            return CollationResult.Unverified;
        }
    }

    /// <summary>
    /// Throws when the database is positively found to be case-sensitive. A database that cannot be checked, and a provider with no
    /// collation, do not fail here.
    /// </summary>
    /// <param name="db">The context whose database is checked. Required.</param>
    /// <param name="logger">Logs a probe that could not run. Null logs nothing.</param>
    /// <param name="failureMessage">The message of the exception, for the app's own wording. Null uses <see cref="DefaultFailureMessage"/>.</param>
    /// <param name="ct">Cancels the probe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="db"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The database compares text case-sensitively.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public static async Task EnsureCaseInsensitiveAsync(DbContext db, ILogger? logger = null, string? failureMessage = null,
        CancellationToken ct = default)
    {
        if (await CheckAsync(db, logger, ct) == CollationResult.CaseSensitive)
        {
            throw new InvalidOperationException(failureMessage ?? DefaultFailureMessage);
        }
    }
}
