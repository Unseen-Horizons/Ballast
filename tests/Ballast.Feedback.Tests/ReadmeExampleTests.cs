// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Feedback;

using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.Feedback.Tests;

/// <summary>
/// Runs the example in src/Ballast.Feedback/README.md exactly as it is written there. The README's using lines are at the top of this
/// file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.Feedback/README.md.
        // A blank message is refused before anything is sent, so this runs without a server.
        using var http = new HttpClient { BaseAddress = new Uri("https://api.example.test/") };
        var client = new FeedbackClient(http, NullLogger<FeedbackClient>.Instance);
        var result = await client.SendAsync(new FeedbackRequest { Message = "   " });
        Console.WriteLine(result.Outcome);

        Assert.Equal(FeedbackSendOutcome.Empty, result.Outcome);
    }
}
