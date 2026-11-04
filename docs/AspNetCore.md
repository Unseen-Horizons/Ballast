# Ballast.AspNetCore: usage guide

Ballast.AspNetCore gives an ASP.NET Core API the same answers for every failure, and the same protection at its edge: a problem
answer with a stable code and a trace reference, exception mappings for the failures an API knows, a 404 for an unknown route under
an API prefix, fixed-window rate limits, security headers, startup checks for secrets, and forwarded headers from a list of proxies.

The package README has the install line and a runnable example. This guide covers every option, every public entry point, and every
way each one fails. Nothing here is set by the library for an application: every value that an application chooses comes in
through an option, a function or an interface.

## How the pieces fit

1. `AddProblemResponses` registers the options, the default titles and the exception handler. `UseProblemResponses` puts the
   handler into the pipeline and gives every response a trace reference.
2. A handler throws an `AppException` (from Ballast.Errors). The handler answers it with its own status, code and message.
3. Any other exception is matched against the `ExceptionMappings`, in order. The first match is answered with its status, code,
   title and optional detail. An exception that no mapping matches is the internal error: status 500, code `INTERNAL_ERROR`, and a
   detail that holds only the trace reference.
4. A validation failure from `[ApiController]` is answered by the framework and passes through the same customisation, so it gets the
   trace reference, the code `VALIDATION_FAILED` and a title from `IProblemTitles`.
5. The rate limiter answers a rejected request with `RATE_LIMITED`, and the security headers go on every response.

## The answer

Every problem answer has the content type `application/problem+json; charset=utf-8`, and the same members in the same order.
This is an illustrative answer for an `AppException` with the code `ITEM_LOCKED` and one extension member `field`:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"The item is locked.","status":409,
 "detail":"Detail text.","instance":"/items/42","code":"ITEM_LOCKED","traceId":"00-4bf92f35-01","field":"X"}
```

- `type`, `title`, `status`, `detail` and `instance` are the RFC 9457 members. A null member is left out. `detail` is absent when
  there is none.
- `code` and `traceId` follow, then any extension members of the exception at the top level.
- A validation answer has no `instance`. Its members are `type`, `title`, `status`, `errors` (field name to messages), `traceId`
  and `code`, in that order:
  `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"Name":["The Name field is required."]},"traceId":"…","code":"VALIDATION_FAILED"}`.
- The trace reference is `Activity.Current?.Id`, or the request's `TraceIdentifier` when there is no activity. The same value is in the
  `X-Trace-Id` header of every response, success or failure, when `UseProblemResponses` is in the pipeline.

## AddProblemResponses

```csharp
public static IServiceCollection AddProblemResponses(this IServiceCollection services, Action<ProblemResponseOptions>? configure = null)
```

Registers `ProblemResponseOptions` (as a singleton, read once), `IProblemTitles` (only when the application has not registered its
own, so a registration made before this call is kept), the framework's problem details customised for the trace reference and
validation, and the exception handler. The options are built here, when the method is called, so an invalid mapping stops the start.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `ExceptionMappings` | `IList<ExceptionMapping>` | empty | The rules for exceptions that are not an `AppException`, checked in order. |
| `EndpointNotFoundDetail` | `Func<HttpContext, string>` | `No endpoint is at this address.` | The detail of the 404 from `MapApiNotFound`. A function, so a text can be localised. |

Ways it fails:

| Input | Exception |
|---|---|
| `services` is null | `ArgumentNullException` |
| An exception mapping is null | `InvalidOperationException`, naming its position |
| An exception mapping has a blank `Title` | `InvalidOperationException`, naming its position and code |
| An exception mapping has `RetryAfterSeconds` below 1 | `InvalidOperationException`, naming its position and code |
| `configure` throws | the exception propagates |

## UseProblemResponses

```csharp
public static IApplicationBuilder UseProblemResponses(this IApplicationBuilder app)
```

Adds a middleware that writes `X-Trace-Id` before the rest of the pipeline runs, on every response, and opens a log scope with a
`TraceId` property for the whole request. It then calls `UseExceptionHandler()`. Place it after `UseForwardedHeaders` (so a log line
carries the caller's real address) and before `UseRateLimiter`, `UseAuthentication` and `UseStaticFiles`, so their failures are answered
as problems too.

Ways it fails:

| Input | Exception |
|---|---|
| `app` is null | `ArgumentNullException` |
| `AddProblemResponses` was not called | `InvalidOperationException`, thrown by the call itself |

When the caller has gone away (the request's `RequestAborted` is cancelled when the exception is handled), the handler answers
**499 with no body**. It is logged at debug and never as an error.

## How an exception is answered

The handler checks, in this order:

1. **The request was abandoned.** Status 499, no body, logged at `Debug` with the method, path and trace reference. The header is still written.
2. **An `AppException`** (or an `UpstreamException`, which is one). Its status, code, message (as the title), detail (or the message
   when it has none) and extensions are used. Mappings are not checked for it. Logged at `Information` below 500 and at `Error` with its
   exception from 500.
3. **The first `ExceptionMapping` whose `Matches` returns true.** The answer takes the mapping's status and code. Its title is, in order:
   `TitleFactory`, then `Title`, then `IProblemTitles` for its status and code. Its detail is `DetailFactory`, or none. Its
   `RetryAfterSeconds` writes the `Retry-After` header. Logged at the mapping's `LogLevel`, or by default at `Information` below 500 and
   at `Error` from 500. The exception and its stack trace go to the log only at `Error` and above. Below that, the log has the exception's
   type and message.
4. **Anything else** is the internal error: `Problem.InternalError`, with the title from `IProblemTitles` for 500 and `INTERNAL_ERROR`.
   Its detail is `Reference {traceId}`. The exception's text is never in the answer, in any environment. It is logged at `Error`, with
   the exception and the trace reference.

If the response has already started, nothing is written. The failure is logged, and a warning says that the answer for the trace
reference was not written. The handler returns true in that case, so the framework does not write a second answer.

## ExceptionMapping

```csharp
public ExceptionMapping(Func<Exception, bool> matches, int status, string code)
```

| Member | Type | Default | Meaning |
|---|---|---|---|
| `Matches` | `Func<Exception, bool>` | (constructor) | Returns true for an exception this rule answers. It receives the exception the handler caught, not its inner exceptions. A rule that needs an inner exception walks `InnerException` itself. |
| `Status` | `int` | (constructor) | The HTTP status, 400 to 599. |
| `Code` | `string` | (constructor) | The stable code. A blank code is refused. |
| `Title` | `string?` | null | A fixed title. A blank title is refused when the options are added. Ignored when `TitleFactory` is set. |
| `TitleFactory` | `Func<HttpContext, Exception, string>?` | null | A computed title. It takes precedence over `Title`. |
| `DetailFactory` | `Func<HttpContext, Exception, string?>?` | null | The detail, or null for none. It is sent to the client, so it must not hold the exception's text unless that text may be shown. |
| `RetryAfterSeconds` | `int?` | null | The `Retry-After` header in seconds. A value below 1 is refused when the options are added. |
| `LogLevel` | `LogLevel?` | null (by status) | The level of the log entry. Set `Warning` for a condition that is expected under load, such as a busy database; the stack trace is then not logged. |

Ways it fails:

| Input | Exception |
|---|---|
| `matches` is null | `ArgumentNullException` |
| `status` is below 400 or above 599 | `ArgumentOutOfRangeException` |
| `code` is null, empty or only white space | `ArgumentException` |

A mapping's `Matches`, `TitleFactory` or `DetailFactory` must not throw. An exception thrown there is not turned into a problem: it
leaves the exception handler, so the request fails outside the answers this library writes.

Mappings are checked for each exception in the order they were added, and each is called at most once per failure.

```csharp
services.AddProblemResponses(options =>
{
    // A timeout from the database is busy, not broken: 503, a Retry-After of 30 seconds, logged without a stack trace.
    options.ExceptionMappings.Add(new ExceptionMapping(error => error is TimeoutException, StatusCodes.Status503ServiceUnavailable, ErrorCodes.ServiceBusy)
    {
        Title = "The service is busy.",
        DetailFactory = (context, _) => "Reference " + ProblemResponses.TraceIdOf(context),
        RetryAfterSeconds = 30,
        LogLevel = LogLevel.Warning,
    });
});
```

A database library's errors are matched by the application's own predicate. This library knows nothing about SQL.

## IProblemTitles

```csharp
public interface IProblemTitles
{
    string Title(int status, string code);
}
```

Gives the title of a problem the library makes: a validation failure, the unknown route, the rate-limit rejection, the internal error, and a
mapping that has neither `TitleFactory` nor `Title`. An `AppException` keeps its own message as its title.

Register an implementation before `AddProblemResponses` (it is then kept), or after it (it replaces the default, because the last
registration wins).

The default, `EnglishProblemTitles`, gives the English reason phrase of the status (`Not Found` for 404, `Too Many Requests` for 429).
A validation failure (`VALIDATION_FAILED`) is titled `One or more validation errors occurred.`, which is the framework's title for it. A
status with no reason phrase is titled `Error`. It never returns null or blank. Its `ValidationTitle` constant holds the validation title.

## MapApiNotFound

```csharp
public static IEndpointConventionBuilder MapApiNotFound(this IEndpointRouteBuilder endpoints, string prefix)
```

Answers any request under the prefix that no endpoint handles with **404 and the code `ENDPOINT_NOT_FOUND`**, as a problem. It is a
fallback route (`{prefix}/{**rest}`), so it takes only what no other endpoint matches. It is anonymous. A request outside the prefix is
not touched: it keeps the application's own fallback, or the framework's 404.

Because the route is a fallback, a request to a known route with a method that route does not support (for example a `POST` to a
`GET`-only route under the prefix) is also answered `ENDPOINT_NOT_FOUND`, and the framework's 405 is not written. This is pinned by a test.

Ways it fails:

| Input | Exception |
|---|---|
| `endpoints` is null | `ArgumentNullException` |
| `prefix` is null, empty or only white space | `ArgumentException` |
| `prefix` does not start with `/`, such as `api` | `ArgumentException` |
| `prefix` is only `/` | `ArgumentException`, since it would match every route |
| `prefix` contains `{`, `}`, `*`, `?` or white space | `ArgumentException` |

A trailing slash is ignored: `/api/` and `/api` name the same prefix. A prefix with several segments, such as `/api/v1`, matches by segment:
`/api/v1x` is not covered.

## ClientAddressKey

```csharp
public static string For(IPAddress? address)
```

The address part of a rate-limit key. An IPv4 address is its own key, and so is an IPv4 address inside an IPv6 mapping (`::ffff:1.2.3.4` is
`1.2.3.4`). Any other IPv6 address is keyed by its first 64 bits, as eight hex digits (upper case, no separators) and `/64`:
`2001:db8:1:2::1` and `2001:db8:1:2::ffff` both have the key `20010DB800010002/64`. A missing address is `unknown`, so every request without
one shares one bucket. It never throws.

## Rate limits

### AddRateLimits

```csharp
public static IServiceCollection AddRateLimits(this IServiceCollection services, IConfiguration configuration, Action<RateLimitOptions>? configure = null)
```

Registers the rate limiter (`AddRateLimiter`) with a fixed-window policy for each entry in `Policies`, and an optional `GlobalPolicy` that
applies to every request. The limits are read from `configuration` now. The application still calls `UseRateLimiter()` after its
authentication. A request over its limit is rejected at once: no request waits in a queue.

`RateLimitOptions`:

| Option | Type | Default | Meaning |
|---|---|---|---|
| `ConfigurationSection` | `string` | `RateLimits` | The section that holds the limits. A policy's limit is read from `{section}:{key}`. |
| `GlobalPolicy` | `FixedWindowPolicy?` | null | A limit for every request. Its key defaults to `{Name}PerMinute`, so a policy named `Global` reads `GlobalPerMinute`. |
| `Policies` | `IList<FixedWindowPolicy>` | empty | The named policies. An endpoint uses one with `[EnableRateLimiting("name")]` or `RequireRateLimiting("name")`. Names are unique. |
| `UserIdClaimTypes` | `IList<string>` | `sub`, then `ClaimTypes.NameIdentifier` | The claims that hold a signed-in user's id, tried in order for the default partition key. |
| `PartitionKey` | `Func<HttpContext, string>?` | null | The partition key of every policy without its own. Null means `user:{id}` for a signed-in caller and `ip:{ClientAddressKey}` for anyone else. |
| `RejectionDetail` | `Func<HttpContext, string>` | `Rate limit exceeded. Retry after the window resets.` | The detail of the 429 answer. A function, so a text can be localised. |

`FixedWindowPolicy`:

| Member | Type | Default | Meaning |
|---|---|---|---|
| (constructor) `name` | `string` | (required) | The policy's name. A blank name is refused. |
| (constructor) `defaultPermitLimit` | `int` | (required) | The limit when the configuration does not set one. It must be at least 1; this is checked by `AddRateLimits`. |
| `ConfigurationKey` | `string?` | null | The key under the section. Null means `{Name}PerMinute`. |
| `Window` | `TimeSpan` | one minute | The length of a window. It must be longer than zero. |
| `PartitionKey` | `Func<HttpContext, string>?` | null | This policy's partition key, or null for the options' one. `RateLimitKeys.ForAddress` makes a policy per address whoever is signed in. |

The limit read from configuration replaces the default. A limit that the configuration does not set uses the default.

Ways it fails (all when `AddRateLimits` is called, so before the application starts serving):

| Input | Exception and message |
|---|---|
| `services` or `configuration` is null | `ArgumentNullException` |
| A configured limit is not a whole number, such as `many` or an empty value | `InvalidOperationException`, naming the policy and the key |
| A limit is below 1, from configuration or from the default | `InvalidOperationException`, naming the policy and the key |
| A window is zero or negative | `InvalidOperationException`, naming the policy |
| Two policies have one name | `InvalidOperationException`, naming the policy |
| `FixedWindowPolicy` is given a blank name | `ArgumentException` |

A request over a limit is answered **429 with the code `RATE_LIMITED`**, as a problem, with the header `Retry-After` in whole seconds (at least 1,
rounded up from the lease's retry time). When the lease gives no retry time, the header is 60.

### Partition keys

- `RateLimitKeys.ForUserOrAddress(context, userIdClaimTypes)`: `user:{id}` for the first of the claims the caller holds, else
  `ip:{ClientAddressKey}`.
- `RateLimitKeys.ForAddress(context)`: `ip:{ClientAddressKey}` whoever is signed in.

Both throw `ArgumentNullException` for a null argument.

## Security headers

### AddSecurityHeaders

```csharp
public static IServiceCollection AddSecurityHeaders(this IServiceCollection services, Action<SecurityHeaderOptions>? configure = null)
```

`SecurityHeaderOptions`:

| Option | Type | Default | Meaning |
|---|---|---|---|
| `ReferrerPolicy` | `string` | `no-referrer` | The `Referrer-Policy` header. |
| `FrameOptions` | `string` | `DENY` | The `X-Frame-Options` header. |
| `PermissionsPolicy` | `string?` | `camera=(), microphone=(), geolocation=()` | The `Permissions-Policy` header, or null for none. |
| `DefaultContentSecurityPolicy` | `string` | `default-src 'none'; frame-ancestors 'none'; base-uri 'self'` | The `Content-Security-Policy` of every response that does not set its own, including a static file whose rules set none. It suits an API, which returns data and no page. |
| `StaticFileRules` | `IList<StaticFileRule>` | empty | The rules for the headers of static files. |

A blank value (`ReferrerPolicy`, `FrameOptions`, `PermissionsPolicy` when set, `DefaultContentSecurityPolicy`, a rule's policy or cache
control, or a rule's header name or value) is refused, and a null rule is refused. Each is an `InvalidOperationException` when the headers are added.

### UseSecurityHeaders

```csharp
public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
```

On every response, before the rest of the pipeline: `X-Content-Type-Options: nosniff`, the referrer policy, the frame options, the
permissions policy (when set), and the default content security policy when the response has none yet. A later component may overwrite any
of them.

Ways it fails: `AddSecurityHeaders` was not called, so `InvalidOperationException` is thrown by the call itself. `app` null is `ArgumentNullException`.

### UseSecureStaticFiles

```csharp
public static IApplicationBuilder UseSecureStaticFiles(this IApplicationBuilder app, Action<StaticFileOptions>? configure = null)
```

Serves the web root with the rules applied to each file. The rules are copied when this method is called, so a rule added later is not used.
A handler the caller sets in `configure` runs after the rules.

`UseSecurityHeaders` must come **before** `UseSecureStaticFiles`. A static file is answered by the static file middleware and does not reach
a component after it, so a static file answered before `UseSecurityHeaders` would have no default content security policy.

Ways it fails: `AddSecurityHeaders` was not called, so `InvalidOperationException` is thrown by the call itself. `app` null is `ArgumentNullException`.

### StaticFileRule

```csharp
public StaticFileRule(string path)
```

| Member | Type | Default | Meaning |
|---|---|---|---|
| `Path` | `string` | (constructor) | The path, starting with `/`. `/` applies to every file. |
| `ExactPath` | `bool` | false | True: only this path. False: this path and what is under it, by segment (`/admin` covers `/admin/x.js`, not `/administrator`). |
| `Suffixes` | `IList<string>` | empty | When not empty, the path must end with one of these, without case: `.html`, or `.html.br` for a precompressed sibling. |
| `ContentSecurityPolicy` | `string?` | null | Written as `Content-Security-Policy`, or left as it is. |
| `CacheControl` | `string?` | null | Written as `Cache-Control`, or left as it is. |
| `Headers` | `IDictionary<string, string>` | empty | Any other headers, overwriting what is there. The names ignore case. |

Every rule that matches a file applies, in the order the rules were added, and a later rule overwrites the header an earlier one set. A file
that no rule matches keeps the default content security policy and writes no `Cache-Control`.

Ways it fails: a `Path` that is null, blank or does not start with `/` raises `ArgumentException` when the rule is built.

Example: the whole web root is not cached, the map pages are cached for an hour, and the HTML pages under the map are not cached. The
later rules overwrite the earlier ones, so the last rule that matches a `.html` file under `/map` gives its `Cache-Control`:

```csharp
builder.Services.AddSecurityHeaders(options =>
{
    options.StaticFileRules.Add(new StaticFileRule("/") { CacheControl = "no-cache" });
    options.StaticFileRules.Add(new StaticFileRule("/map") { CacheControl = "public, max-age=3600" });
    options.StaticFileRules.Add(new StaticFileRule("/map")
    {
        Suffixes = { ".html" },
        CacheControl = "no-cache",
    });
});
```

## Startup checks

### RequireSecret

```csharp
public static string RequireSecret(IConfiguration configuration, string key, int minBytes, params string[] placeholders)
```

Returns the secret at `key`, or stops the start. It refuses a missing or blank value, a value equal to one of the `placeholders` (without case, so `CHANGE-ME`
and `change-me` both refuse), and a value shorter than `minBytes` in UTF-8. The message names the key, says how to set it (user secrets,
or the environment variable with `__` in place of `:`), and never shows the value.

Ways it fails:

| Input | Exception |
|---|---|
| `configuration` is null | `ArgumentNullException` |
| `key` is null, empty or only white space | `ArgumentException` |
| `minBytes` is below 1 | `ArgumentOutOfRangeException` |
| The value is missing or blank | `InvalidOperationException`: `{key} must be configured.` |
| The value is a placeholder | `InvalidOperationException`: `{key} still holds a placeholder value.` |
| The value is shorter than `minBytes` | `InvalidOperationException`: `{key} must be at least {minBytes} bytes long (UTF-8).` |

### AddForwardedHeadersFromNetworks

```csharp
public static IServiceCollection AddForwardedHeadersFromNetworks(this IServiceCollection services, IConfiguration configuration, Action<ForwardedNetworkOptions>? configure = null)
```

Reads the list of proxy networks at `ForwardedHeaders:KnownNetworks` (CIDR strings such as `10.0.0.0/8` or `fd00::/8`), and configures the framework's
`ForwardedHeadersOptions` so that `X-Forwarded-For` and `X-Forwarded-Proto` are read only from a hop in one of those networks. The application
still calls `UseForwardedHeaders()`.

`ForwardedNetworkOptions`:

| Option | Type | Default | Meaning |
|---|---|---|---|
| `ConfigurationKey` | `string` | `ForwardedHeaders:KnownNetworks` | The key of the list. |
| `TrustAnySenderWhenEmpty` | `bool` | false | With an empty list: false reads no forwarded header; true reads them from any sender, once the framework's loopback-only default is cleared. |

With an empty list and the default, no forwarded header is read, so a caller's `X-Forwarded-For` cannot choose its address or its rate-limit
bucket. A deployment that relies on forwarded headers and has not yet filled in its list must either fill it in or set
`TrustAnySenderWhenEmpty`, and the second keeps the old permissive behaviour.

Ways it fails:

| Input | Exception |
|---|---|
| `services` or `configuration` is null | `ArgumentNullException` |
| An entry is blank | `InvalidOperationException`, naming the entry's position |
| An entry is not a network that `IPNetwork.TryParse` accepts, such as `10.0.0.0/33` or a word | `InvalidOperationException`, naming the position and the value. The entry is refused, never skipped. |

### EachStringLengthAttribute

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class EachStringLengthAttribute(int max) : ValidationAttribute
```

Every string in a list is at most `Max` characters long. A null list and a null item pass. A value that is not a list of strings
(`IEnumerable<string?>`) also passes, so the attribute checks only lists of strings, and another attribute must check the type of anything else.
A failure makes the model invalid, and the framework answers 400 with `VALIDATION_FAILED`.

Put it on a property with `{ get; init; }`, as in the example below. On a positional record, a bare attribute lands on the
constructor parameter, not on the property the framework validates, so write it as `[property: EachStringLength(200)]` there.

```csharp
public sealed record SearchRequest
{
    [MaxLength(2)]
    [EachStringLength(200)]
    public IReadOnlyList<string>? Earlier { get; init; }
}
```

## Hand-built validation and problems

### ProblemResponses

| Member | Meaning |
|---|---|
| `const string ContentType` | `application/problem+json; charset=utf-8` |
| `const string TraceIdHeader` | `X-Trace-Id` |
| `string TraceIdOf(HttpContext)` | The trace reference of the request. |
| `Task WriteAsync(HttpContext, Problem, CancellationToken)` | Writes a `Problem` (from Ballast.Errors) as the answer, with its status and the trace header. |
| `ValidationProblemDetails CreateValidation(HttpContext, IReadOnlyDictionary<string, string[]>)` | The validation answer, built and not written. |
| `Task WriteValidationAsync(HttpContext, IReadOnlyDictionary<string, string[]>, CancellationToken)` | Writes the validation answer with status 400. Its JSON is the same as the framework's for the same errors. |

`CreateValidation` and `WriteValidationAsync` throw `ArgumentNullException` for a null context, a null errors dictionary or a null message list.
They throw `ArgumentException` when there are no fields, a field name is blank, or a field has no message. `WriteAsync` and
`WriteValidationAsync` throw `InvalidOperationException` when the response has already started.

## What the library writes on the wire, per status

| Case | Status | Code | Title | Detail | Extras |
|---|---|---|---|---|---|
| `AppException` | its own, 400 to 599 | its own | its message | its detail, or its message | its extensions |
| Mapped exception | the mapping's | the mapping's | `TitleFactory`, `Title`, or `IProblemTitles` | `DetailFactory`, or none | `Retry-After` when set |
| Unmatched exception | 500 | `INTERNAL_ERROR` | `Internal Server Error` (via `IProblemTitles`) | `Reference {traceId}` | none |
| Abandoned request | 499 | none | none | none | no body |
| Validation (framework) | 400 | `VALIDATION_FAILED` | `IProblemTitles` (English: `One or more validation errors occurred.`) | none | `errors` |
| Unknown route under the prefix | 404 | `ENDPOINT_NOT_FOUND` | `IProblemTitles` (`Not Found`) | `EndpointNotFoundDetail` | none |
| Rate limit | 429 | `RATE_LIMITED` | `IProblemTitles` (`Too Many Requests`) | `RejectionDetail` | `Retry-After` |

Every row has `instance` set to the request path, except validation (the framework does not set it) and the abandoned request (no body).

## Sample wiring

```csharp
var builder = WebApplication.CreateBuilder(args);

var jwtKey = StartupChecks.RequireSecret(builder.Configuration, "Jwt:Key", 32, "CHANGE-ME");

builder.Services.AddControllers();
builder.Services.AddProblemResponses(options =>
{
    options.ExceptionMappings.Add(new ExceptionMapping(error => error is TimeoutException, 503, ErrorCodes.ServiceBusy)
    {
        Title = "The service is busy.",
        RetryAfterSeconds = 30,
        LogLevel = LogLevel.Warning,
    });
});
builder.Services.AddRateLimits(builder.Configuration, options =>
{
    options.Policies.Add(new FixedWindowPolicy("Auth", 10));
    options.Policies.Add(new FixedWindowPolicy("Lookup", 60) { PartitionKey = RateLimitKeys.ForAddress });
});
builder.Services.AddSecurityHeaders();
builder.Services.AddForwardedHeadersFromNetworks(builder.Configuration);

var app = builder.Build();
app.UseForwardedHeaders();
app.UseProblemResponses();
app.UseSecurityHeaders();
app.UseDefaultFiles();
app.UseSecureStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.MapControllers();
app.MapApiNotFound("/api");
app.Run();
```

## Departures from the application code

These are the places where the library does not reproduce what an application did before, or reproduces it only with a setting.
Each one is a deliberate choice; an application that depends on the old behaviour must set the option named here.

1. **Content type.** Every problem answer is `application/problem+json; charset=utf-8`. The framework's JSON writer replaces a preset content
   type with `application/json`, so the library sets it explicitly. An answer that was `application/json` now has the problem type. A client
   that checks the type must accept `application/problem+json`.
2. **Forwarded headers with no proxy list.** With an empty `ForwardedHeaders:KnownNetworks`, the library reads no forwarded header. The
   framework's own default is also to read none, so this departs only from an application that set the forwarded header flags itself
   without a list. Such an application sets `TrustAnySenderWhenEmpty` to keep its behaviour, or fills in the list.
3. **Static file rules.** Every matching rule applies, in the order added, and a later rule overwrites an earlier one. It is not a
   first-match table. This reproduces the combinations the applications use; a rule set written for first-match must be reordered.
4. **Default API content security policy.** The library's default is `default-src 'none'; frame-ancestors 'none'; base-uri 'self'`. An
   application that used another string keeps it by setting `DefaultContentSecurityPolicy`.

## Azure and the known proxy networks

On Azure App Service the requests reach the application through the platform's front end. Unless the forwarded headers are read from a
listed network, the address the application sees is the front end's, so every caller of that host shares one rate-limit bucket per front
end address. The list at `ForwardedHeaders:KnownNetworks` must name the networks the platform's front end sends from.

Whether Azure publishes a fixed list of those networks is **NEEDS VERIFICATION**: it has not been established here, and the owner must
confirm the Azure configuration before an Azure host relies on forwarded headers. Until then, leave the list empty (no forwarded header is read)
or set `TrustAnySenderWhenEmpty` only where every request reaches the application through a proxy that sets the headers.

## Packaging and what is not set here

The library is packed as the `Ballast.AspNetCore` package (the install line is in the README). It references the ASP.NET Core shared
framework (`Microsoft.AspNetCore.App`), which adds no package version, and Ballast.Errors. It is not marked trimmable or AOT-compatible,
because the MVC types it uses are not trim-safe, as the project file says.

Nothing in the library names an application, a route, a configuration section other than the defaults above, a text or a code that an
application owns. An application's own codes and texts go in its mappings, its `IProblemTitles` and its options.
