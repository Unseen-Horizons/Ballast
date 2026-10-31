// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Ballast.Errors;

namespace Ballast.Errors.Tests;

/// <summary>
/// Runs the example in src/Ballast.Errors/README.md exactly as it is written there. The README's using lines are at the top of
/// this file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.Errors/README.md.
        // The server throws this: a stable code, a message, and the detail for the developer.
        var error = AppException.NotFound(ErrorCodes.NotFound, "Station not found.", "No station has id 42.");

        // The answer the API writes. The trace reference joins it to the server's log.
        var problem = Problem.From(error, traceId: "00-4bf92f35-01", instance: "/stations/42");
        Console.WriteLine($"{problem.Status} {problem.Code} {problem.Type}");

        // The client reads the answer and translates the code, not the message.
        var json = """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Station not found.","status":404,"code":"NOT_FOUND"}""";
        using var response = new HttpResponseMessage { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };
        var info = await ProblemReader.TryReadAsync(response);
        Console.WriteLine(info?.Code);

        Assert.Equal("NOT_FOUND", info?.Code);
        Assert.Equal(404, problem.Status);
    }
}
