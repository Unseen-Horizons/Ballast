# Ballast.Errors: usage guide

Ballast.Errors gives an API three things: an error type that carries a stable code and an HTTP status, the problem answer
that the API writes for it (RFC 9457), and a reader that a client uses to read that answer. It depends on .NET alone, so a
server and a phone app can both use it.

The package README has the install line and a runnable example. This guide covers every type and member, and every way
each one fails.

## How the pieces fit

1. A handler throws an `AppException`, or an `UpstreamException` for a failed call to another service. Each carries a
   stable code from `ErrorCodes` (or the API's own) and the status it means.
2. The API turns the exception into a `Problem` with `Problem.From`. An unexpected exception becomes `Problem.InternalError`,
   which carries only a trace reference and never the exception's text.
3. The API writes the problem as its HTTP answer. Ballast.AspNetCore is planned to do this step. Until then, the API writes
   the members itself.
4. The client calls `ProblemReader.TryReadAsync` and gets a `ProblemInfo` with the code, title, detail and trace reference.

A client translates the `Code`. It shows the `Title` only when it has no translation for that code.

## AppException

```csharp
public class AppException : Exception
```

A deliberate refusal, such as a missing item, an invalid request or a refused credential. It has no parameterless or
message-only constructor, because every error needs a code and a status.

### Constructor

```csharp
public AppException(string code, string message, int status, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null)
```

| Parameter | Required | Meaning |
|---|---|---|
| `code` | yes | The stable code. Use a value from `ErrorCodes`, or the API's own. |
| `message` | yes | The message. It becomes the problem title. |
| `status` | yes | The HTTP status, from 400 to 599. There is no default, so each throw says what it means. |
| `detail` | no | The developer sentence about this occurrence. When null, the problem detail is the message. |
| `extensions` | no | Extra members for the answer. The dictionary is copied, and each name is checked. |

Ways it fails. Nothing is replaced by a default.

| Input | Exception | `ParamName` |
|---|---|---|
| `code` is null | `ArgumentNullException` | `code` |
| `code` is empty or only white space | `ArgumentException` | `code` |
| `message` is null | `ArgumentNullException` | `message` |
| `message` is empty or only white space | `ArgumentException` | `message` |
| `detail` is empty or only white space (null is allowed) | `ArgumentException` | `detail` |
| `status` is below 400 or above 599 | `ArgumentOutOfRangeException` | `status` |
| An extension name is empty or only white space | `ArgumentException` | `extensions` |
| An extension name is a problem member name: `type`, `title`, `status`, `detail`, `instance`, `code` or `traceId`, in any letter case | `ArgumentException` (its message names the member) | `extensions` |

A `code` that does not match any `ErrorCodes` value is accepted. The library does not check the code against a list. The status 418 and any
other value from 400 to 599 is accepted.

```csharp
var error = new AppException(ErrorCodes.ValidationFailed, "The date is in the past.", 422, "Departure 2020-01-01 has passed.");
```

### Properties

| Member | Type | Meaning |
|---|---|---|
| `Message` (inherited) | `string` | The title of the problem. Never blank. |
| `Code` | `string` | The stable error code. |
| `StatusCode` | `int` | The HTTP status, from 400 to 599. |
| `Detail` | `string?` | The developer sentence, or null. Null makes `Problem.From` use the message as the detail. |
| `Extensions` | `IReadOnlyDictionary<string, object?>` | The extra members. Never null: it is empty when none were given. |

### Factories

Each factory takes `(string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null)`
and fails exactly as the constructor does for `code`, `message`, `detail` and `extensions`.

| Method | Status | Use |
|---|---|---|
| `BadRequest` | 400 | The request is malformed or invalid. |
| `Forbidden` | 403 | The caller is known but may not do this. |
| `NotFound` | 404 | The item does not exist. |
| `Conflict` | 409 | The request conflicts with the current state of the item. |
| `Unprocessable` | 422 | The request is well formed, but its content cannot be processed. |
| `Upstream` | 502 | A call to another service failed. Use `UpstreamException` when the host, status and address should be kept. |

```csharp
var error = AppException.NotFound(ErrorCodes.NotFound, "Station not found.", "No station has id 42.");
```

The status set by a factory cannot be changed afterwards. A different status needs a new exception.

## UpstreamException

```csharp
public class UpstreamException : AppException
```

A failed call to an upstream service. It is an `AppException` with status 502. It keeps the upstream's host, the status the
upstream answered with, and the address that was called, for logs and diagnostics.

### Constructor

```csharp
public UpstreamException(string host, int upstreamStatusCode, string requestUri, string code, string message, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null)
```

| Parameter | Meaning |
|---|---|
| `host` | The upstream's host name. Required. |
| `upstreamStatusCode` | The status the upstream answered with. It is stored as given and is not checked. |
| `requestUri` | The address that was called. Required. It may be written to logs, so remove any secret, such as a key in the query, before passing it in. |
| `code`, `message`, `detail`, `extensions` | As for `AppException`. |

Ways it fails:

| Input | Exception | `ParamName` |
|---|---|---|
| `host` is null, empty or only white space | `ArgumentNullException` or `ArgumentException` | `host` |
| `requestUri` is null, empty or only white space | `ArgumentNullException` or `ArgumentException` | `requestUri` |
| `code`, `message`, `detail` or `extensions` fails as in `AppException` | as in `AppException` | as in `AppException` |

### Properties

| Member | Type | Meaning |
|---|---|---|
| `Host` | `string` | The upstream's host name. |
| `UpstreamStatusCode` | `int` | The status the upstream answered with, as given. |
| `RequestUri` | `string` | The address that was called. |

`Problem.From` writes none of `Host`, `UpstreamStatusCode` or `RequestUri`. Anything the client may see belongs in the
detail or the extensions.

```csharp
var error = new UpstreamException("api.example.test", 503, "https://api.example.test/v1/stations", ErrorCodes.UpstreamUnavailable, "The feed did not answer.");
```

## ErrorCodes

```csharp
public static class ErrorCodes
```

The codes that any API can use. Each is a `const string`.

| Constant | Value | Meaning |
|---|---|---|
| `NotFound` | `NOT_FOUND` | The item asked for does not exist. |
| `ValidationFailed` | `VALIDATION_FAILED` | A value in the request is missing or invalid. |
| `UpstreamUnavailable` | `UPSTREAM_UNAVAILABLE` | A service this API depends on could not be reached, or did not give a usable answer. |
| `InternalError` | `INTERNAL_ERROR` | An unexpected failure on the server. Its answer carries only a trace reference. |
| `RateLimited` | `RATE_LIMITED` | The caller has sent too many requests in a period. |
| `ServiceBusy` | `SERVICE_BUSY` | The server is busy with a shared resource, such as the database, and the request can be tried again. |
| `EndpointNotFound` | `ENDPOINT_NOT_FOUND` | The URL names no endpoint. |

The library does not tie a code to a status. The API chooses the status for each throw. The table above says what each code
means, not which status it uses.

A code is a public contract. Once released, a code is never renamed, reused or changed. A new condition gets a new code, and
an API adds its own codes beside these. A client translates the code, so a changed code breaks translations that are already
shipped.

## Problem

```csharp
public sealed record Problem
```

The answer an API writes for a failed request. It holds the members RFC 9457 describes, plus the stable code, the trace
reference and any extension members. It is a plain record with no framework dependency.

| Member | Type | Meaning |
|---|---|---|
| `Type` (required) | `string` | The problem type address, from `ProblemTypes.For`. |
| `Title` (required) | `string` | The short title. For an `AppException`, its message. |
| `Status` (required) | `int` | The HTTP status. |
| `Detail` | `string?` | The developer sentence about this occurrence, or null. |
| `Instance` | `string?` | The address of the request that failed, or null. |
| `Code` (required) | `string` | The stable error code. |
| `TraceId` | `string?` | The trace reference that joins this answer to the server's log, or null. |
| `Extensions` | `IReadOnlyDictionary<string, object?>` | Extra members. Never null: it is empty when there are none. |

`Type`, `Title`, `Status` and `Code` are `required`: an object initializer that leaves one out does not compile. Build a
`Problem` with `From` or `InternalError`, and set the members yourself only for a problem those two cannot describe.

The record is the shape, not the wire format. Serializing it directly with System.Text.Json puts `Extensions` under an
`extensions` member. The HTTP form, which is planned to be written by Ballast.AspNetCore, puts each extension at the top level,
as ASP.NET's ProblemDetails does.

### Problem.From

```csharp
public static Problem From(AppException exception, string? traceId = null, string? instance = null)
```

| Problem member | Value |
|---|---|
| `Type` | `ProblemTypes.For(exception.StatusCode)` |
| `Title` | `exception.Message` |
| `Status` | `exception.StatusCode` |
| `Detail` | `exception.Detail`, or `exception.Message` when the detail is null |
| `Instance` | `instance` |
| `Code` | `exception.Code` |
| `TraceId` | `traceId` |
| `Extensions` | `exception.Extensions` (shared, read-only) |

When `traceId` or `instance` is null, the member is null. The failure modes are:

| Input | Exception |
|---|---|
| `exception` is null | `ArgumentNullException` |

For an `UpstreamException`, `From` writes what an `AppException` has, and none of its host, status or address. The
`UpstreamException` section above lists those three.

```csharp
var problem = Problem.From(AppException.NotFound(ErrorCodes.NotFound, "Station not found.", "No station has id 42."), traceId: "00-4bf92f35-01", instance: "/stations/42");
// problem.Title  == "Station not found."
// problem.Detail == "No station has id 42."
// problem.Status == 404
```

### Problem.InternalError

```csharp
public static Problem InternalError(string traceId, string? instance = null)
```

The answer for an unexpected failure. It is always:

| Problem member | Value |
|---|---|
| `Type` | the 500 address in RFC 9110, section 15.6.1 |
| `Title` | `Internal Server Error` |
| `Status` | 500 |
| `Detail` | `Reference {traceId}` |
| `Instance` | `instance` |
| `Code` | `INTERNAL_ERROR` |
| `TraceId` | `traceId` |
| `Extensions` | empty |

The method takes no exception. A database error number, a stack trace or any other text of the failure cannot reach the
client, because nothing from the failure is passed in. The failure belongs in the server's log, under the same trace reference.

| Input | Exception |
|---|---|
| `traceId` is null | `ArgumentNullException` |
| `traceId` is empty or only white space | `ArgumentException` |

```csharp
var problem = Problem.InternalError("00-4bf92f35-01", "/stations/42");
// problem.Detail == "Reference 00-4bf92f35-01"
```

## ProblemTypes

```csharp
public static class ProblemTypes
```

### ProblemTypes.For

```csharp
public static string For(int status)
```

Returns the problem type address for an HTTP status. It never throws and never returns null. A status that RFC 9110 defines
has its section of that document. Status 429 has its section of RFC 6585. Every other status, 418 included, returns
`about:blank`, the RFC 9457 default for a problem with no type of its own.

| Status | Type address |
|---|---|
| 400 | `https://tools.ietf.org/html/rfc9110#section-15.5.1` |
| 401 | `https://tools.ietf.org/html/rfc9110#section-15.5.2` |
| 402 | `https://tools.ietf.org/html/rfc9110#section-15.5.3` |
| 403 | `https://tools.ietf.org/html/rfc9110#section-15.5.4` |
| 404 | `https://tools.ietf.org/html/rfc9110#section-15.5.5` |
| 405 | `https://tools.ietf.org/html/rfc9110#section-15.5.6` |
| 406 | `https://tools.ietf.org/html/rfc9110#section-15.5.7` |
| 407 | `https://tools.ietf.org/html/rfc9110#section-15.5.8` |
| 408 | `https://tools.ietf.org/html/rfc9110#section-15.5.9` |
| 409 | `https://tools.ietf.org/html/rfc9110#section-15.5.10` |
| 410 | `https://tools.ietf.org/html/rfc9110#section-15.5.11` |
| 411 | `https://tools.ietf.org/html/rfc9110#section-15.5.12` |
| 412 | `https://tools.ietf.org/html/rfc9110#section-15.5.13` |
| 413 | `https://tools.ietf.org/html/rfc9110#section-15.5.14` |
| 414 | `https://tools.ietf.org/html/rfc9110#section-15.5.15` |
| 415 | `https://tools.ietf.org/html/rfc9110#section-15.5.16` |
| 416 | `https://tools.ietf.org/html/rfc9110#section-15.5.17` |
| 417 | `https://tools.ietf.org/html/rfc9110#section-15.5.18` |
| 421 | `https://tools.ietf.org/html/rfc9110#section-15.5.20` |
| 422 | `https://tools.ietf.org/html/rfc9110#section-15.5.21` |
| 426 | `https://tools.ietf.org/html/rfc9110#section-15.5.22` |
| 429 | `https://tools.ietf.org/html/rfc6585#section-4` |
| 500 | `https://tools.ietf.org/html/rfc9110#section-15.6.1` |
| 501 | `https://tools.ietf.org/html/rfc9110#section-15.6.2` |
| 502 | `https://tools.ietf.org/html/rfc9110#section-15.6.3` |
| 503 | `https://tools.ietf.org/html/rfc9110#section-15.6.4` |
| 504 | `https://tools.ietf.org/html/rfc9110#section-15.6.5` |
| 505 | `https://tools.ietf.org/html/rfc9110#section-15.6.6` |
| any other, for example 418, 499 or 0 | `about:blank` |

The section numbers were checked against RFC 9110 and RFC 6585. A new status is added to the table when RFC 9110 defines it.

## ProblemInfo

```csharp
public sealed record ProblemInfo(string? Code, string? Title, string? Detail = null, string? TraceId = null);
```

The fields of a problem answer that a client reads. A field is null when the answer does not carry it. Records compare by value.

| Member | Meaning |
|---|---|
| `Code` | The error code, for the client to translate. Null when the answer had none, for example a bare string. |
| `Title` | The server's title, for when the client has no translation for the code. |
| `Detail` | The developer sentence about the occurrence. |
| `TraceId` | The trace reference that joins the client's log to the server's. |

## ProblemReader

```csharp
public static class ProblemReader
```

### ProblemReader.TryReadAsync

```csharp
public static async Task<ProblemInfo?> TryReadAsync(HttpResponseMessage response, CancellationToken ct = default)
```

Reads the problem fields from the answer's body. Only a JSON string value is read.

Accepted shapes:

| Body | Result |
|---|---|
| An object with `code`, `title`, `detail` and `traceId` as strings | Each field as it is. |
| An object with no string `title`, but a string `message` | `Title` is the `message`. |
| An object with no string `title` or `message`, but a string `error` | `Title` is the `error`. |
| An object whose `title` is a string and whose `message` is also a string | `Title` is the `title`. The `message` is ignored. |
| A bare JSON string, such as `"Something went wrong."` | `Title` is the string, `Code` is null. |
| A bare JSON string that is only white space | Null. |

Other rules:

- A member whose value is not a string is ignored. `"code": 5` gives a null `Code`, and a non-string `title` falls back to
  `message` and then to `error`.
- A code is never taken from a title, and a title is never taken from a code.
- Member names are case-sensitive: `Code` is not read as `code`.
- The reader translates nothing.

Null results:

Null means the answer has no problem fields. It does not mean the request succeeded. The reader returns null when the body
is:

- empty or only white space,
- not JSON, including HTML from a proxy,
- a JSON value that is neither an object nor a string, such as an array, a number, `true` or `null`,
- an object with none of the members above as a string,
- JSON nested more than 64 levels deep, which the parser refuses.

Ways it fails:

| Situation | Result |
|---|---|
| `response` is null | `ArgumentNullException` |
| `ct` is cancelled while the body is read | `OperationCanceledException` |
| The body stream fails, for example the connection drops during the read, or the response was already disposed | The exception the stream throws (for example `HttpRequestException` or `ObjectDisposedException`) propagates. The reader does not catch it. |
| The body is not valid JSON | Null, as above. |

The body is read once. `HttpClient` buffers the body by default, so the reader and a later read both see it. With
`HttpCompletionOption.ResponseHeadersRead` the body is a live stream, and reading it here uses it up. The reader does not
dispose the response.

```csharp
var json = """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Station not found.","status":404,"code":"NOT_FOUND"}""";
using var response = new HttpResponseMessage { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };
var info = await ProblemReader.TryReadAsync(response);
// info?.Code == "NOT_FOUND"
```

## Compatibility with earlier answers

Ballast.Errors keeps the members that installed clients read: `code`, `title`, `detail` and `traceId`. An answer written by an
older API, with `message`, `error` or a bare string, still gives a title. A new client against an old server therefore still
shows a readable sentence rather than a blank label or raw JSON.

## Changes from the earlier error types

These are the changes from the error types that Ballast.Errors was built from:

- The code and the status are required. The earlier types allowed a missing code, and a default status.
- Status 403 has its RFC 9110 section 15.5.4 address, and status 429 has RFC 6585 section 4. The earlier address table had 403 as
  402's section and 429 as a section that RFC 9110 does not have.
- A status with no section has `about:blank`, not an address built from the number.
- An extension whose name is a problem member is refused. The earlier code dropped such an extension without a message.
- `InternalError` takes no exception, so the failure's text cannot reach the answer.
