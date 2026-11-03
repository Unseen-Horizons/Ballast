# Ballast.Feedback

Feedback from an app's users, in three parts: the request a user sends, with the caps on each field; a manager that takes the
message in, checks it, stores it through an interface, and reads it back for triage; and a client that posts the message from the
app and reports how the send ended. It has no ASP.NET Core reference, so a MAUI app can use the client and the contract, and the
API uses the manager.

Install:

```
dotnet add package Ballast.Feedback --prerelease
```

## Example

```csharp
using Ballast.Feedback;
using Microsoft.Extensions.Logging.Abstractions;

// A blank message is refused before anything is sent, so this runs without a server.
using var http = new HttpClient { BaseAddress = new Uri("https://api.example.test/") };
var client = new FeedbackClient(http, NullLogger<FeedbackClient>.Instance);
var result = await client.SendAsync(new FeedbackRequest { Message = "   " });
Console.WriteLine(result.Outcome);
```

The example prints `Empty`. In the app, the client posts to the API's `feedback` route and gets back one of six outcomes:
`Sent`, `Empty`, `NotConfigured`, `NotAvailable`, `Rejected` or `Unreachable`. The app supplies the words it shows for each.

The API side is a `FeedbackManager` over the app's own `IFeedbackStore`, behind the app's own endpoint. The usage guide,
`docs/Feedback.md` in the repository, has every option with its default, every public member, and every way it fails.
