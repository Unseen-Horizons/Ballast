// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.Feedback;
using Ballast.Maui.Feedback;

namespace Ballast.Maui.Tests;

/// <summary>The device side of feedback: the app's version and platform travel with the message, and a blank message is not sent.</summary>
public class FeedbackServiceTests
{
    private static FeedbackService Make(ScriptedApi api, string appVersion = "0.9.0", string platform = "android") =>
        new(new FeedbackClient(new HttpClient(api) { BaseAddress = new Uri("https://api.test/") }), appVersion, platform);

    [Fact]
    public async Task The_message_goes_out_with_the_app_version_and_platform_the_app_passed_in()
    {
        var api = new ScriptedApi(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var service = Make(api);

        var result = await service.SendAsync("The map froze", "Map", "rider@example.com");

        Assert.Equal(FeedbackSendOutcome.Sent, result.Outcome);
        var body = Assert.Single(api.Bodies);
        Assert.Contains("\"0.9.0\"", body, StringComparison.Ordinal);
        Assert.Contains("\"android\"", body, StringComparison.Ordinal);
        Assert.Contains("The map froze", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blank_message_is_not_sent()
    {
        var api = new ScriptedApi(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var service = Make(api);

        var result = await service.SendAsync("   ", null, null);

        Assert.Equal(FeedbackSendOutcome.Empty, result.Outcome);
        Assert.Empty(api.Bodies);
    }
}
