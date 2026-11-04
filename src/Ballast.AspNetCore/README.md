# Ballast.AspNetCore

The ASP.NET Core house rules for an API: a problem answer with a stable code and a trace reference on every failure, exception
mappings for the failures an API knows, a 404 for an unknown route under an API prefix, fixed-window rate limits, security
headers, startup checks for secrets, and forwarded headers from a list of proxies. Every value an application chooses comes in
through its options. It depends on ASP.NET Core and on Ballast.Errors.

Install:

```
dotnet add package Ballast.AspNetCore --prerelease
```

## Example

The example runs against an in-memory request, so it needs no server. An API calls the same two methods from its own handlers.

```csharp
using Ballast.AspNetCore;
using Ballast.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// Registered once, at startup. A timeout from the database is answered 503 with a Retry-After of 30 seconds.
var services = new ServiceCollection();
services.AddProblemResponses(options =>
{
    options.ExceptionMappings.Add(new ExceptionMapping(error => error is TimeoutException, 503, ErrorCodes.ServiceBusy)
    {
        Title = "The service is busy.",
        RetryAfterSeconds = 30,
    });
});
await using var provider = services.BuildServiceProvider();

// A request as the pipeline holds it. The API's handler throws an AppException, and the answer is written from it.
var http = new DefaultHttpContext { RequestServices = provider };
http.TraceIdentifier = "00-4bf92f35-01";
http.Request.Path = "/items/42";
http.Response.Body = new MemoryStream();
var error = AppException.NotFound(ErrorCodes.NotFound, "Item not found.", "No item has id 42.");
await ProblemResponses.WriteAsync(http, Problem.From(error, ProblemResponses.TraceIdOf(http), http.Request.Path.Value));

http.Response.Body.Position = 0;
Console.WriteLine($"{http.Response.StatusCode} {new StreamReader(http.Response.Body).ReadToEnd()}");
```

The example prints the status and the answer's JSON: `404` followed by the problem, with the code `NOT_FOUND` and the trace
reference.

## Protecting a stored credential

`SecretProtector` encrypts a credential before it is stored and decrypts it where it is used. The purpose string is the application's
own, kept for good: a payload protected under one purpose does not open under another.

```csharp
using Ballast.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

var services = new ServiceCollection();
services.AddDataProtection();
services.AddLogging();
services.AddSecretProtector("Example.Credentials");
using var provider = services.BuildServiceProvider();
var protector = provider.GetRequiredService<SecretProtector>();

// Stored encrypted: the database never holds the plain value.
var stored = protector.Protect("bearer-abc-123")!;
Console.WriteLine(stored.StartsWith("enc:v1:", StringComparison.Ordinal) && !stored.Contains("bearer-abc-123"));
Console.WriteLine(protector.Unprotect(stored));

// A payload from another purpose does not open. The read goes on without the credential, and the warning names the source.
var other = new SecretProtector(provider.GetRequiredService<IDataProtectionProvider>(), NullLogger<SecretProtector>.Instance,
    new SecretProtectorOptions { Purpose = "Example.Other" });
var read = other.UnprotectForRead(stored, "Feed North", out var warning);
Console.WriteLine($"{read is null} {warning}");
```

The example prints `True`, then `bearer-abc-123`, then `True Feed North: its stored credential could not be decrypted, so the request was sent
without it. Re-enter the credential on the source.`

The usage guide, with every option, the wiring of a real host, the unreadable modes and every way the library fails, is `docs/AspNetCore.md` in the repository.
