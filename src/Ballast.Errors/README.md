# Ballast.Errors

An error type with a stable code and an HTTP status, the problem answer an API writes for it (RFC 9457), and a reader that
reads that answer on the client. It depends on .NET alone, so a server and a phone app can both use it.

Install:

```
dotnet add package Ballast.Errors --prerelease
```

## Example

```csharp
using System.Text;
using Ballast.Errors;

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
```

The example prints `404 NOT_FOUND https://tools.ietf.org/html/rfc9110#section-15.5.5` and then `NOT_FOUND`.

The usage guide, with every option and every way the library fails, is `docs/Errors.md` in the repository.
