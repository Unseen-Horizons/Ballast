# Ballast.Email

Sends an e-mail through SendGrid's web API, with the rules for when a send counts as delivered, a shape check for
addresses, and a send-once ledger that records each mail so that it goes out at most once. It calls SendGrid through
`HttpClient`, with no SendGrid SDK.

Install:

```
dotnet add package Ballast.Email --prerelease
```

## Example

```csharp
using Ballast.Email;
using Microsoft.Extensions.Logging.Abstractions;

// No API key here, so nothing is sent. Outside Development the send reports NotConfigured, and nothing reaches SendGrid.
var email = new EmailService(new MailSettings(apiKey: null, fromEmail: null), NullLogger<EmailService>.Instance);
var result = await email.SendAsync("klijent@example.test", "Your invoice is ready", "Your invoice is ready to download.");
Console.WriteLine(result);

Console.WriteLine(EmailAddress.IsPlausible("klijent@example.test"));
```

The example prints `NotConfigured` and then `True`.

To send for real, put the API key and a verified sender address in configuration, and call `AddBallastEmail` at startup.
The setup of the SendGrid account and the key is in `docs/setup/sendgrid.md`. The usage guide, with every option and every
way the library fails, is `docs/Email.md` in the repository.
