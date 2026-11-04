// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Email;

using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.Email.Tests;

/// <summary>
/// Runs the example in src/Ballast.Email/README.md exactly as it is written there. The README's using lines are at the top of
/// this file, because a test method cannot hold them.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        // The example, copied verbatim from src/Ballast.Email/README.md.
        // No API key here, so nothing is sent. Outside Development the send reports NotConfigured, and nothing reaches SendGrid.
        var email = new EmailService(new MailSettings(apiKey: null, fromEmail: null), NullLogger<EmailService>.Instance);
        var result = await email.SendAsync("klijent@example.test", "Your invoice is ready", "Your invoice is ready to download.");
        Console.WriteLine(result);

        Console.WriteLine(EmailAddress.IsPlausible("klijent@example.test"));

        Assert.Equal(EmailSendResult.NotConfigured, result);
        Assert.True(EmailAddress.IsPlausible("klijent@example.test"));
    }
}
