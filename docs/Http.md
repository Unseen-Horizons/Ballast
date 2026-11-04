# Ballast.Http: usage guide

Ballast.Http gives an app the outbound half of the safety rules it needs when it fetches addresses it does not control: a feed
URL an operator typed in, a public page it reads, a log line that carries a URL. It refuses a request to a non-public address,
follows redirects by one rule set, reads a body under a size cap, sends a per-host credential only where it belongs, writes a
URL to a log without its secrets, honours a busy server's Retry-After, and reads public pages politely.

It depends on the Microsoft.Extensions packages only (`Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience` and
`Microsoft.Extensions.Configuration.Abstractions`). It has no ASP.NET Core dependency.

The package README has the install line and a runnable example. This guide covers every type and member, and every way each one
fails.

## How the pieces fit

1. `AddSafeHttpClient` registers a named `HttpClient` with automatic redirects off, the public-address check at connect time,
   the app's User-Agent and a timeout.
2. `RedirectFollower` sends a request through that client and follows its redirects by hand, checking each hop before it is
   sent. `PoliteFetcher` does the same for a public page, after its robots.txt.
3. A refusal is an `HttpSafetyException` with an `HttpSafetyReason`, or a `PoliteRefusal` in a result. The app maps the reason
   to its own error code. The library has no app codes.
4. `CappedBody` reads a body under a byte cap. `HostCredentials` reads the per-host credentials from configuration.
   `UrlForLog` redacts a URL for a log. `RetryAfter` and `AddRetryAfterResilience` honour Retry-After.

Nothing needs setting up outside the code: no key, no portal and no DNS record.

No redirect is ever followed by the client itself. An automatic follow would send a credential to a hop that has not been
judged. `RedirectFollower` and `PoliteFetcher` rely on their named client having automatic redirects off, which
`AddSafeHttpClient` and `AddPoliteFetcher` set. A client made any other way is not checked (see Notes).

## Registering the client

### AddSafeHttpClient

```csharp
public static IHttpClientBuilder AddSafeHttpClient(this IServiceCollection services, string name, Action<SafeHttpClientOptions> configure)
```

Registers the named client. The returned builder takes `AddRetryAfterResilience`, and the host may configure the handler after
this call.

#### SafeHttpClientOptions

| Option | Default | Meaning |
|---|---|---|
| `UserAgent` | none, required | The User-Agent every request sends: a product token with an optional comment, for example `ExampleApp/1.0 (+https://example.test/app)`. There is no default, so the library never sends a name that is not the app's own. |
| `Timeout` | 60 seconds | The most time one request may take, from the send to the last byte of the response headers. Must be above zero. |
| `AllowNonPublicAddresses` | false | Development only. Skips the connect-time check, so loopback and private addresses are reachable. Keep it false in production. |

#### Ways it fails

Registration fails at once, and nothing is registered:

| Input | Exception |
|---|---|
| `services` or `configure` is null | `ArgumentNullException` |
| `name` is empty or only white space | `ArgumentException` |
| `UserAgent` is empty or only white space | `ArgumentException` |
| `UserAgent` is not a valid product token with an optional comment | `ArgumentException` |
| `Timeout` is zero or below | `ArgumentException` |

When a request goes out, a host that resolves to a non-public address is refused at connect time (see PublicDestinationGuard).
The handler raises that refusal inside an `HttpRequestException`, whose `InnerException` is the `HttpSafetyException`.
`RedirectFollower` and `PoliteFetcher` unwrap it. A caller that uses the client directly sees the wrapped form.

## RedirectFollower

```csharp
public RedirectFollower(IHttpClientFactory httpFactory, string clientName, RedirectFollowerOptions? options = null)
public Task<HttpResponseMessage> SendAsync(RedirectRequest request, CancellationToken ct = default)
public static bool IsRedirect(HttpStatusCode status)
```

`IsRedirect` is true for 301, 302, 303, 307 and 308. A 300 or a 305 is an answer, not a redirect.

`RedirectRequest` is a record:

| Member | Default | Meaning |
|---|---|---|
| `Url` (constructor) | required | The first address. |
| `Method` | GET | The method. A 307 or 308 keeps it. A 301, 302 or 303 turns it into a GET. |
| `Authorization` | none | The `Authorization` header, for a request that carries a credential. It is sent only on the hops the rules allow. |
| `Body` | none | A factory for the body. It is called once for each hop that sends a body, so a redirect that keeps the body gets fresh content. |

`SendAsync` returns the first response that is not a followed redirect. It may have any status. The caller reads it and disposes
it. The redirect responses are disposed inside `SendAsync`.

### RedirectFollowerOptions

| Option | Default | Meaning |
|---|---|---|
| `MaxRedirects` | 5 | The most redirects one request may follow. A chain of six redirects sends six requests and no more. Must not be below zero. |
| `RequireHttps` | true | Every address in the chain must be https. When false, plain http is allowed, but an https address still never redirects to http. |
| `AllowNonPublicAddresses` | false | Development only. Skips the public-address check before each request. It must match `SafeHttpClientOptions.AllowNonPublicAddresses` on the client. |

### The rules

The first address is checked before anything is sent. It must be an absolute http or https URL with no user information. A request
that carries a credential must be https. With `RequireHttps`, the address must be https.

Each address is then checked in this order, for the first address and for every redirect target:

1. The public-address check (`PublicDestinationGuard.EnsurePublicDestination`) runs before the request is sent, unless
   `AllowNonPublicAddresses` is set.
2. A redirect has a Location header that is a URL.
3. The redirect is within `MaxRedirects`.
4. The target is an http or https URL with no user information.
5. An https address never redirects to http.
6. With `RequireHttps`, the target is https.
7. A request with a credential keeps the same host and the same port, and its target is https.
8. A 307 or 308 with a body keeps the same host.
9. A 301, 302 or 303 becomes a GET with no body. A 307 or 308 keeps the method and the body.

Each refusal is an `HttpSafetyException`:

| Reason | When |
|---|---|
| `InvalidUrl` | The first address is not an absolute http or https URL. |
| `UserInfoInUrl` | An address has user information (`user:password@`). |
| `CredentialNotHttps` | A request with a credential has a first address that is not https, or is redirected to a non-https address. |
| `SchemeNotAllowed` | With `RequireHttps`, an address is not https. Or a redirect goes to a scheme other than http or https. |
| `SchemeDowngrade` | An https address redirects to http. |
| `CredentialOffHost` | A request with a credential is redirected to another host. |
| `CredentialOtherPort` | A request with a credential is redirected to another port. |
| `BodyOffHost` | A 307 or 308 with a body is redirected to another host. |
| `TooManyRedirects` | The chain would be longer than `MaxRedirects`. |
| `MissingLocation` | A redirect has no Location header. |
| `InvalidLocation` | A redirect's Location is present but is not a URL that can be used. |
| `NonPublicAddress` | An address is not public, before the request or at connect time. |
| `HostNotResolved` | The host does not resolve, so its address cannot be checked. |

Other failures from `SendAsync`:

- `ArgumentNullException` when `request` or `request.Url` is null.
- `HttpRequestException` when a host cannot be reached for a reason other than the safety rules. The handler's exception is passed on unchanged.
- `TaskCanceledException` when the client's `Timeout` ends a request. When the caller's `ct` is cancelled, it is `OperationCanceledException` instead.

Constructor failures: `ArgumentNullException` for `httpFactory`, `ArgumentException` for a blank `clientName`, and
`ArgumentOutOfRangeException` for a `MaxRedirects` below zero.

## PublicDestinationGuard

The guard has two checks. The pre-flight check (`EnsurePublicDestination`) gives a clear answer before any connection. The
connect-time check (`ConnectToPublicOnlyAsync`) is the one that enforces the rule, because it checks the address the socket
actually connects to. A pre-flight check alone can be beaten by a name that answers with a public address first and a private
address on the next lookup.

| Member | Meaning |
|---|---|
| `void EnsurePublicDestination(string url)` | Refuses a URL that is not an absolute http or https URL, or whose host does not resolve to public addresses only. A literal IP address is judged as it is. A name is resolved, and every address it gives must be public. |
| `ValueTask<Stream> ConnectToPublicOnlyAsync(SocketsHttpConnectionContext context, CancellationToken ct)` | The connect callback for a `SocketsHttpHandler`. It connects only when every candidate address is public, so a host that publishes one public and one private address is refused. `AddSafeHttpClient` installs it unless `AllowNonPublicAddresses` is set. |
| `bool IsInternal(IPAddress address)` | Whether an address is not public. It is true for loopback, link-local, private (10/8, 172.16/12, 192.168/16), carrier-grade NAT (100.64/10), 0/8, 192.0.0/24, the benchmarking range (198.18/15), multicast, the reserved and broadcast range (224 and up), IPv6 loopback, link-local, site-local, multicast, unique local (fc00::/7) and the unspecified address. An IPv4 address carried inside an IPv6 one is judged as the IPv4 address it carries: the mapped form, NAT64 (64:ff9b::/96), 6to4 (2002::/16) and the old compatible form. |

Ways they fail:

| Call | Failure |
|---|---|
| `EnsurePublicDestination` with a null `url` | `ArgumentNullException` |
| `EnsurePublicDestination` with text that is not an absolute http or https URL | `HttpSafetyException`, reason `InvalidUrl` |
| `EnsurePublicDestination` with a name that does not resolve | `HttpSafetyException`, reason `HostNotResolved` |
| `EnsurePublicDestination` when any address is not public, or there is no address | `HttpSafetyException`, reason `NonPublicAddress` |
| `ConnectToPublicOnlyAsync` when any candidate address is not public | `HttpSafetyException`, reason `NonPublicAddress`, raised inside an `HttpRequestException` by the handler |
| `ConnectToPublicOnlyAsync` when a name does not resolve, or the connection fails | The resolver's or the socket's exception, raised by the handler as an `HttpRequestException` |
| `IsInternal` with a null address | `ArgumentNullException` |

## HttpSafetyException and HttpSafetyReason

```csharp
public HttpSafetyException(HttpSafetyReason reason, string message, Uri? url = null, Exception? inner = null)
public HttpSafetyReason Reason { get; }
public string? RedactedUrl { get; }
```

`Reason` says why. `RedactedUrl` is the URL the refusal is about, through `UrlForLog`, or null when there was none. The message
names hosts and what was refused. It never names a credential.

The reason values are stable: a value is never renamed, reused or removed once released. The reasons are listed under
RedirectFollower and PublicDestinationGuard above. Each app maps a reason to its own error code.

## UrlForLog

```csharp
public const string NotAnAbsoluteUrl = "(not an absolute URL)";
public const string RedactedQuery = "?[redacted]";
public static string Redact(string url)
public static string Redact(Uri uri)
```

The one rule for a URL that goes into a log, a warning, an audit row or an exception: the scheme, host, port and path are kept,
and the query is replaced by `?[redacted]` when there was one. The port is shown only when it is not the scheme's default. User
information and the fragment are dropped. Text that is not an absolute URL with a host is never echoed. It becomes
`(not an absolute URL)`.

| Input | Output |
|---|---|
| `https://user:secret@feeds.example.test/gtfs.zip?apikey=SECRET` | `https://feeds.example.test/gtfs.zip?[redacted]` |
| `http://127.0.0.1:8080/x` | `http://127.0.0.1:8080/x` |
| `https://example.test:443/a#part` | `https://example.test/a` |
| `/relative/path` | `(not an absolute URL)` |

Ways it fails: a null argument is an `ArgumentNullException`. Text that is not an absolute URL, or a URL with no host, gives the
placeholder and throws nothing.

## CappedBody

```csharp
public static Task<byte[]> ReadBytesAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct = default)
public static Task<string> ReadStringAsync(HttpResponseMessage response, long maxBytes, Action<string>? onWarning = null, CancellationToken ct = default)
public static string DecodeText(byte[] bytes, string? declaredCharset, ICollection<string>? warnings, string label)
```

The body is read as bytes, so the cap holds whatever the server declared. A declared length over the cap is refused before a
byte is read. A body that reads past the cap is stopped as soon as one read goes over it. `ReadBytesAsync` returns the bytes.
`ReadStringAsync` decodes them with the declared charset, and strips a byte order mark that belongs to the chosen encoding.

`DecodeText` decodes a document's bytes for the readers that take text. A UTF-8 or UTF-16 byte order mark decides the encoding
and is stripped. Otherwise a declared charset is used. With neither, the bytes must be valid UTF-8. When they are not, a warning
gives the number of bytes replaced, and the text is decoded with replacement characters. Nothing is silently decoded as another
encoding. The legacy code pages (windows-1250 and the rest) are registered once, for the whole process.

Ways they fail:

| Case | Result |
|---|---|
| A declared length over `maxBytes`, or more bytes read than `maxBytes` | `BodyTooLargeException`, whose `MaxBytes` is the cap. Nothing is returned. |
| A negative `maxBytes` | `ArgumentOutOfRangeException` |
| A null `response`, `bytes` or `label` | `ArgumentNullException` |
| A declared charset the platform does not know | Not an exception. `ReadStringAsync` calls `onWarning` and reads UTF-8. `DecodeText` adds a warning and reads UTF-8. |
| Invalid UTF-8 with no declared charset | A warning, not an exception. The bytes are decoded with replacement characters. |
| The caller's `ct` is cancelled during the read | `OperationCanceledException`. It is not reported as a cap. |
| The connection fails while the body streams | The read's exception, unchanged |

## HostCredentials

```csharp
public static IReadOnlyDictionary<string, string> Load(IConfiguration configuration, HostCredentialsOptions options)
```

Reads the basic-auth credential for each host from configuration, never from a database or from a URL. The result is keyed by
host, case-insensitively. It holds the credentials, so never log it.

There are two shapes, and they mean the same thing. The indexed list:

```
BasicAuthHosts:0:Host       = feeds.example.test
BasicAuthHosts:0:Credential = username:password
```

Use the indexed list on App Service. There an application setting name allows only letters, numbers and underscores, and a
host has dots, so the host goes in the value. As an environment variable the same entry is `BasicAuthHosts__0__Host`.

The host-keyed map, read only when `MapSectionName` is set:

```
FeedCredentials:feeds.example.test = username:password
```

When both name one host, the indexed list wins.

### HostCredentialsOptions

| Option | Default | Meaning |
|---|---|---|
| `SectionName` | `BasicAuthHosts` | The indexed section. Must not be blank. Each app names its own. |
| `MapSectionName` | null | The host-keyed section. Null reads none. |

Ways it fails:

- A null `configuration` or `options` is an `ArgumentNullException`.
- A blank `SectionName` is an `ArgumentException`.
- A map entry with no credential is an `InvalidOperationException`, naming the entry.
- An indexed entry with only one of `Host` and `Credential` is an `InvalidOperationException`, naming the entry.

`Load` fails when it is called. An app that calls it at startup therefore fails at startup, rather than at a fetch that later gets a bare 401.

## Retry-After

### RetryAfter.Read

```csharp
public static TimeSpan? Read(HttpResponseMessage response, TimeSpan max)
public static TimeSpan? Read(HttpResponseMessage response, TimeSpan max, TimeProvider clock)
```

Reads the Retry-After header in either form RFC 9110 allows. A delta is the wait itself. A date is the wait from the clock's
current instant to that date. A date that has already passed gives zero, so the request may be sent again now. A wait longer than
`max` is cut to `max`. The result is null when there is no usable header, so the caller uses its own wait.

Ways it fails: a null `response` or `clock` is an `ArgumentNullException`. A `max` below zero is an `ArgumentOutOfRangeException`.

### AddRetryAfterResilience

```csharp
public static IHttpClientBuilder AddRetryAfterResilience(this IHttpClientBuilder builder, Action<RetryAfterOptions>? configure = null)
public const string HandlerName = "Ballast.RetryAfter";
```

Adds a retry to the client's pipeline. A status in `RetryStatuses`, or a connection failure, is tried again, up to `MaxAttempts`
tries in all. Each retry waits for the answer's Retry-After, cut to `MaxRetryAfter`. When the answer gives none, the retry uses
the next entry of `DefaultDelays`, and past the end of that list, its last entry. The handler is named `HandlerName`.

#### RetryAfterOptions

| Option | Default | Meaning |
|---|---|---|
| `MaxAttempts` | 3 | The most tries, the first one included. So the default is the first try and two retries. 1 means no retry, and then no handler is added. Must be at least 1. |
| `MaxRetryAfter` | 60 seconds | The longest Retry-After wait honoured. Must not be below zero. |
| `DefaultDelays` | 10 seconds, then 30 seconds | The waits used when the answer gives no Retry-After. Must not be empty or hold a negative delay. |
| `RetryStatuses` | 429, 502, 503, 504 | The statuses that are tried again. Any other status is the answer, at once. |
| `RetryOnConnectionFailure` | true | Whether a connection failure is tried again. |

Not tried again: a status that is not in `RetryStatuses`, a safety refusal (an `HttpRequestException` that wraps an
`HttpSafetyException`), a client timeout, and a cancellation by the caller.

After the last try, the last response is returned, so the caller still sees its status. A last connection failure is thrown.

Ways it fails at registration: a null `builder` is an `ArgumentNullException`. A `MaxAttempts` below 1, or a `MaxRetryAfter` below
zero, is an `ArgumentOutOfRangeException`. An empty `DefaultDelays`, or one with a negative delay, is an `ArgumentException`.

## Polite fetcher

### AddPoliteFetcher

```csharp
public static IServiceCollection AddPoliteFetcher(this IServiceCollection services, string name, Action<PoliteFetcherOptions> configure)
```

Registers a `PoliteFetcher` under `name`, as a keyed singleton, so one app can run more than one crawler identity. Resolve it with
`GetRequiredKeyedService<PoliteFetcher>(name)`. It also registers the named client it reads through, made by
`AddSafeHttpClient`. That client's User-Agent is `{ProductToken}/{Version} (+{BotInfoUrl})`, and its timeout is the option's
`Timeout`.

#### PoliteFetcherOptions

| Option | Default | Meaning |
|---|---|---|
| `ProductToken` | none, required | The crawler's name, made of letters, digits, `-`, `_` and `.`, for example `ExampleBot`. robots.txt groups are matched on it. |
| `Version` | `1.0` | The version in the User-Agent. Must be a valid product version. |
| `BotInfoUrl` | none, required | An absolute https URL where a site can read what the crawler does. It appears in the User-Agent. |
| `MaxCrawlDelay` | 60 seconds | A robots.txt Crawl-delay longer than this refuses the page, and nothing is waited for. Must not be below zero. |
| `Timeout` | 60 seconds | The most time one request may take. Must be above zero. |
| `BlockedHosts` | none | Bare host names that are never requested. A host equal to an entry, or ending in `.` and the entry, is blocked, ignoring case. A trailing dot is ignored on both sides. Each entry must be a bare host name, with no URL, port or white space. |

Fixed values, which are public constants and not options:

| Constant | Value | Meaning |
|---|---|---|
| `PoliteFetcher.MaxRedirects` | 5 | The most redirects one fetch follows, for a page and for robots.txt. |
| `PoliteFetcher.MinimumGap` | 3 seconds | The least gap between two requests to one host. |
| `PoliteFetcher.RobotsCacheFor` | 24 hours | How long one origin's robots.txt answer is kept. |
| `PoliteFetcher.MaxRobotsBytes` | 500 KiB | The most robots.txt that is read. A longer file is cut off, and its prefix is used. |

Ways registration fails: a null `services` or `configure` is an `ArgumentNullException`. A blank `name` is an `ArgumentException`.
So is a `ProductToken` that is missing or has any other character, a `Version` that is not a valid version, a `BotInfoUrl` that
is not an absolute https URL, a `Timeout` at or below zero, a negative `MaxCrawlDelay`, and a `BlockedHosts` entry that is not a
bare host name. An `ArgumentException` is also raised by the named client's registration.

### PoliteFetcher

```csharp
public PoliteFetcher(IHttpClientFactory httpFactory, string clientName, PoliteFetcherOptions options, TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? wait = null)
public Task<PoliteFetchResult> GetAsync(Uri url, long maxBytes, CancellationToken ct = default)
public Task<PoliteFetchResult> GetToAsync(Uri url, long maxBytes, Stream destination, CancellationToken ct = default)
public static bool IsBlockedHost(string host, IReadOnlyList<string> blockedHosts)
```

`time` is the clock for the robots cache and the pacing; null uses the system clock. `wait` is the wait between two requests to one
host; null uses `Task.Delay`, and a test passes its own. A `PoliteFetcher` is registered as a singleton and is safe to use from
many requests at once, and it paces requests to each host.

`GetAsync` reads the page into memory. `GetToAsync` writes it into `destination`. On success, `GetToAsync`'s result has an empty
`Body`, and the bytes are in the stream. A `TooLarge` refusal may leave part of the body written, so the caller discards the
stream's content. Nothing is written when the page is refused or answers with an error status.

Each fetch goes through these steps in order:

1. The URL is https and has no user information. Otherwise the result is `InvalidUrl`, and nothing is requested.
2. Each address, the page's and each redirect target's, is checked. A blocked host gives `BlockedHost`, before its address is
   looked up. A non-public address gives `NotPublic` for the page, and `RedirectRefused` for a redirect.
3. robots.txt for the origin is read from the cache while it is fresh, and fetched otherwise. A robots.txt that cannot be used
   disallows everything.
4. robots.txt disallows the path: `RobotsDisallowed`, and the page is not requested.
5. A Crawl-delay longer than `MaxCrawlDelay`: `CrawlDelayTooLong`, and nothing is waited for.
6. The request starts at least `MinimumGap` after the previous request to the same host ended, or after that host's Crawl-delay
   when it is longer. The gap covers the body too.
7. A redirect with a usable Location is followed by the redirect rules below. A non-2xx answer is returned as a result, with its
   `Status` and an empty `Body`, and no refusal.
8. A declared length over `maxBytes`, or a read past it, gives `TooLarge`.
9. A 2xx answer gives the result, with its `Body`.

The page's redirects follow the same rules as `RedirectFollower` with `RequireHttps` on and no downgrade to http. A redirect
with no usable Location is refused as `RedirectRefused`. robots.txt is read over http as well. Its redirects may go to http,
and a refused robots redirect disallows everything.

#### The result

```csharp
public sealed record PoliteFetchResult(PoliteRefusal? Refusal, string Detail, HttpStatusCode? Status, Uri FinalUrl, byte[] Body, TimeSpan? CrawlDelayAsked = null)
public bool Refused => Refusal is not null;
public string Text => Encoding.UTF8.GetString(Body);
```

| Member | Meaning |
|---|---|
| `Refusal` | Why the page was not fetched. Null when the page was read. |
| `Detail` | A sentence saying why, in words. |
| `Status` | What the host answered, when it answered. |
| `FinalUrl` | The last URL reached after the redirects. |
| `Body` | The body for a 2xx answer. Empty otherwise, and empty for `GetToAsync`. |
| `CrawlDelayAsked` | The Crawl-delay the host asked for, when the refusal is `CrawlDelayTooLong`. |

#### PoliteRefusal

| Refusal | When |
|---|---|
| `InvalidUrl` | The URL is not https, or has user information. Nothing is requested. |
| `BlockedHost` | The host of the page or of a redirect target is on the blocked list. Nothing is requested from it, not even robots.txt. |
| `NotPublic` | The page's address is not public. |
| `RedirectRefused` | A redirect is refused: to plain http, to a URL with user information, to a non-public address, to a URL that is not one, with no usable Location, or past `MaxRedirects`. |
| `RobotsDisallowed` | robots.txt disallows the path, or robots.txt could not be used. That covers a 5xx answer, a failed or timed-out fetch, a refused or Location-less redirect, a robots.txt host that is blocked or not public, and more than `MaxRedirects` redirects. |
| `CrawlDelayTooLong` | robots.txt asks for a Crawl-delay over `MaxCrawlDelay`. |
| `TooLarge` | The body is declared over `maxBytes`, or read past it. Nothing is kept. |
| `Unreachable` | The host could not be reached: a name that does not resolve, a refused or reset connection, or a connection that drops while the body is read. |
| `TimedOut` | The host did not answer within `Timeout`. |

A 4xx or 5xx answer for the page is not a refusal. It is a result with `Status` set, no `Refusal`, and an empty `Body`.

Ways the calls fail:

- A null `url` or `destination` is an `ArgumentNullException`.
- A negative `maxBytes` is an `ArgumentOutOfRangeException`, and nothing is requested.
- When the caller's `ct` is cancelled, `OperationCanceledException` is thrown. It is not turned into a result.
- Every other failure above is a result with a `Refusal`, not an exception.

The constructor fails with an `ArgumentNullException` for a null `httpFactory` or `options`, and an `ArgumentException` for a blank
`clientName` or for an option that `AddPoliteFetcher` also refuses.

`IsBlockedHost` is an `ArgumentNullException` for a null host or list. It compares as described under `BlockedHosts`.

## RobotsTxt

```csharp
public static RobotsTxt Parse(string text, string productToken)
public static RobotsTxt FromStatus(int status, string body, string productToken)
public static RobotsTxt AllowAll { get; }
public static RobotsTxt DisallowAll(string reason)
public bool IsAllowed(string pathAndQuery)
public TimeSpan? CrawlDelay { get; }
public string? Reason { get; }
```

`Parse` reads a robots.txt for one crawler. It uses the group whose user-agent is the crawler's product token, matched without
regard to case, and otherwise the `*` group. With neither, everything is allowed. Within the group, the rule whose pattern is
longest and matches the path decides. On a tie, Allow wins. A path that matches no rule is allowed. A pattern may use `*` for any
run of characters, and a trailing `$` to anchor the end. A leading byte order mark is ignored.

`FromStatus` reads the answer to a robots.txt request. A 2xx is parsed. A 4xx allows everything, with a reason. Any other status,
such as a 5xx or a 3xx with no usable Location, disallows everything, with a reason.

`IsAllowed` takes the path and query, such as `/a/b?c=d`. An empty path is read as `/`. `CrawlDelay` is the group's Crawl-delay,
or null. `Reason` says why the answer was not a parsed file. It is null for a parsed file.

Ways they fail:

- `Parse` with a null `text` is an `ArgumentNullException`.
- `Parse` with a blank `productToken` is an `ArgumentException`. `FromStatus` raises the same exception for a 2xx answer, which is the only case that parses.
- `IsAllowed` with a null path is an `ArgumentNullException`.

`AllowAll` allows every path, with no delay. `DisallowAll(reason)` disallows every path, and keeps the reason.

## Development-only flags

`SafeHttpClientOptions.AllowNonPublicAddresses` and `RedirectFollowerOptions.AllowNonPublicAddresses` switch the public-address
check off. With either one set, a request may reach loopback and private addresses, which is the case the guard exists to stop.
Keep both false outside development. When the client is set to allow them, the follower must be set to allow them too.

## Notes

- `RequireHttps` defaults to true. A public source that is served over plain http is therefore refused at its first address,
  unless the app sets `RequireHttps` to false. Setting it to false still refuses an https address that redirects to http.
- With the default `MaxRedirects` of 5, one request sends at most six requests.
- A client made by `AddSafeHttpClient` or `AddPoliteFetcher` has automatic redirects off. A client made any other way is not
  checked by this library. `RedirectFollower` and `PoliteFetcher` trust it to have automatic redirects off, and an automatic
  follow would send a credential to a hop that has not been judged.
- A URL that embeds a credential is refused, both as the first address and as a redirect target. A credential is sent only as
  `RedirectRequest.Authorization`, on the hops the rules allow.
- Write a URL to a log through `UrlForLog`. `HttpSafetyException.RedactedUrl` is already redacted.
- `AddRetryAfterResilience` is opt-in. Nothing retries unless the client is given it.
