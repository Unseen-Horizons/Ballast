// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Test targets for semgrep/ballast-csharp.yml. Not compiled: no project includes this folder.
// A line marked "ruleid: <id>" must be flagged by that rule. A line marked "ok: <id>" must not be flagged by it.

namespace SemgrepTargets;

public sealed class Targets
{
    public void Sql(DbContext db, int id, string name)
    {
        // ruleid: ballast-csharp-raw-sql-interpolated
        db.Database.ExecuteSqlRaw($"DELETE FROM Orders WHERE Id = {id}");

        // ok: ballast-csharp-raw-sql-interpolated
        db.Database.ExecuteSqlRaw("DELETE FROM Orders WHERE Id = {0}", id);

        // ruleid: ballast-csharp-raw-sql-concatenated
        db.Database.ExecuteSqlRaw("DELETE FROM Orders WHERE Name = " + name);

        // ok: ballast-csharp-raw-sql-concatenated
        db.Database.ExecuteSqlInterpolated($"DELETE FROM Orders WHERE Id = {id}");
    }

    public void Http(IHttpClientFactory factory)
    {
        // ruleid: ballast-csharp-new-httpclient
        var direct = new HttpClient();

        // ok: ballast-csharp-new-httpclient
        var named = factory.CreateClient("payments");
    }

    public TokenValidationParameters Tokens()
    {
        // ruleid: ballast-csharp-jwt-validation-off
        var loose = new TokenValidationParameters { ValidateLifetime = false };

        // ok: ballast-csharp-jwt-validation-off
        var strict = new TokenValidationParameters { ValidateLifetime = true, ValidateAudience = true };
        return strict;
    }

    public void Log(ILogger logger, string password, string userId)
    {
        // ruleid: ballast-csharp-secret-in-log
        logger.LogInformation("Signing in with password {Password}", password);

        // ok: ballast-csharp-secret-in-log
        logger.LogInformation("Signed in user {UserId}", userId);
    }

    public void Start(string command, string arguments)
    {
        // ruleid: ballast-csharp-process-start-nonconstant
        System.Diagnostics.Process.Start(command, arguments);

        // ruleid: ballast-csharp-process-start-nonconstant
        System.Diagnostics.Process.Start( command);

        // ok: ballast-csharp-process-start-nonconstant
        System.Diagnostics.Process.Start("git", "--version");
    }

    public void Tls(HttpClientHandler handler)
    {
        // ruleid: ballast-csharp-tls-certificate-validation-off
        handler.ServerCertificateCustomValidationCallback = (sender, certificate, chain, errors) => true;

        // ok: ballast-csharp-tls-certificate-validation-off
        handler.ServerCertificateCustomValidationCallback = (sender, certificate, chain, errors) => errors == System.Net.Security.SslPolicyErrors.None;
    }
}
