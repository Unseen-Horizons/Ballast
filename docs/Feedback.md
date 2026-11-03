# Ballast.Feedback: usage guide

Ballast.Feedback takes a user's message in and keeps it for triage, and it sends that message from the app. It has three parts:
the request contract with the caps on each field, a manager that checks a message and reads it back for triage over an
interface the app implements, and a client that posts the message and reports how the send ended. It depends on .NET, on
`Microsoft.Extensions.Logging.Abstractions`, and on Ballast.Errors and Ballast.Paging. It has no ASP.NET Core reference and no
Entity Framework Core reference, so a MAUI app can reference the contract and the client, and the API uses the manager.

The package README has the install line and a runnable example. This guide covers every type and member, the defaults of every
option, and every way each one fails.

## How the pieces fit

1. The app's mobile client builds a `FeedbackRequest`, with the message and the text the user gave, and the app's own app
   version and platform. `FeedbackClient` trims it, posts it to the API, and reports a `FeedbackSendResult`.
2. The API's anonymous endpoint (kept in the app) receives the same `FeedbackRequest` and calls `FeedbackManager.SubmitAsync`.
   The manager checks it, and saves it through the app's `IFeedbackStore`.
3. Triage reads the list through `FeedbackManager.ListAsync` and changes a row's status with `SetStatusAsync`. The endpoints that
   call these stay in the app, with the app's authorization.

The library does not write routes, does not own the database, and does not know what the categories mean. Those are the app's.

## Caps

The caps are constants in `FeedbackLimits`, in characters. They are caps rather than expectations: an endpoint that anyone can
call must not take an unbounded text field.

| Field | Cap | Constant |
|---|---|---|
| `Message` | 4000 | `FeedbackLimits.MessageMaxLength` |
| `Subject` | 200 | `FeedbackLimits.SubjectMaxLength` |
| `Email` | 256 | `FeedbackLimits.EmailMaxLength` |
| `AppVersion` | 64 | `FeedbackLimits.AppVersionMaxLength` |
| `Platform` | 64 | `FeedbackLimits.PlatformMaxLength` |
| `Category` | 64 | `FeedbackLimits.CategoryMaxLength` |
| `ContextName` | 200 | `FeedbackLimits.ContextNameMaxLength` |

A value over its cap is refused by `FeedbackManager.SubmitAsync`, never cut down. The caps are measured on the text as it was
sent, before trimming. The client does not refuse over-cap text itself: the server's refusal comes back as `Rejected`.

## Contract

### `FeedbackRequest`

A class with settable properties. Its JSON names are the names the apps already send, and they do not change:

| Property | JSON name | Type | Rule |
|---|---|---|---|
| `Message` | `message` | string | Required. A blank message is refused. |
| `Subject` | `subject` | string? | Optional. |
| `Email` | `email` | string? | Optional. Must be an e-mail address when present (`EmailAddressAttribute`). |
| `AppVersion` | `appVersion` | string? | Optional. The app's version as the app reports it. |
| `Platform` | `platform` | string? | Optional. The platform as the app reports it. |
| `Category` | `category` | string? | Optional. Checked against `FeedbackOptions.Categories` when that list is set. |
| `ContextId` | `stationId` | int? | Optional. The id of the item the report was filed from. Attribution only. |
| `ContextName` | `stationName` | string? | Optional. The name of that item. |

The JSON names `stationId` and `stationName` are the wire names the apps send. The C# names are `ContextId` and `ContextName`
because the library is not about maps, and an app names its own item.

### `FeedbackResponse`

The row the admin list returns. Its JSON names are `id`, `message`, `subject`, `email`, `category`, `appVersion`, `platform`,
`stationId`, `stationName`, `receivedAt` and `status`. `receivedAt` is in UTC. `status` is the name of a `FeedbackStatus`.

### `FeedbackStatus`

`New`, `InReview`, `Handled`, `Dismissed`. The store keeps the name (`ToString()`). The members and their order do not change:
the values are stored and sent as they are.

### `FeedbackCodes`

The fallback codes the client gives an outcome when the server gave no code: `FeedbackEmpty`, `FeedbackNotConfigured`,
`FeedbackNotAvailable`, `FeedbackRejected` and `FeedbackUnreachable`. They are the names the apps have always logged.

## Options: `FeedbackOptions`

Set on the constructors of `FeedbackManager` and `FeedbackClient`. An option that is not set keeps its default.

| Option | Default | Used by | Meaning |
|---|---|---|---|
| `Route` | `feedback` | `FeedbackClient` | The path the client posts to, relative to its base address. Must not be blank. |
| `Categories` | empty (any category) | `FeedbackManager` | The categories a report may carry, matched exactly. Empty accepts any category up to its cap. When it lists names, another category is refused. A report with no category, or with a blank one, is always accepted. |
| `InvalidCode` | `FEEDBACK_INVALID` | `FeedbackManager` | The code of the 400 for a blank message, a value over its cap, or a reply address that is not an address. |
| `UnknownCategoryCode` | `FEEDBACK_CATEGORY_UNKNOWN` | `FeedbackManager` | The code of the 400 for a category that is not in `Categories`. |

`FeedbackOptions` is validated when a manager or client is built: a blank `Route`, `InvalidCode` or `UnknownCategoryCode`, or a
blank name in `Categories`, throws `ArgumentException` at construction.

## Server: `IFeedbackStore`

The app implements this interface over its own database. The library has no Entity Framework Core, so a new column or table is
the app's migration, not the library's.

| Member | Contract |
|---|---|
| `Task<int> AddAsync(NewFeedback feedback, CancellationToken ct = default)` | Saves the message and returns the id the store gave it. Throws when the save fails. |
| `Task<PagedResult<FeedbackResponse>> ListAsync(FeedbackListQuery query, CancellationToken ct = default)` | Returns one page, newest first, with the total the filters match. Build it with `new PagedResult<FeedbackResponse>(rows, total, query.Page)`, where the rows start at `query.Page.Skip`. |
| `Task<bool> SetStatusAsync(int id, FeedbackStatus status, CancellationToken ct = default)` | Sets the status and returns true, or returns false when there is no row with that id. The store also clears any review mark the app keeps on the row, because a new status replaces the earlier review. |

The contract is strict: a save that did not happen must throw. A store that returns a made-up id after a failed save would make the
app say "saved" when nothing was saved.

### `NewFeedback`

The message as the manager hands it to the store, after trimming and checking: `Message`, `Subject`, `Email`, `Category`,
`AppVersion`, `Platform`, `ContextId`, `ContextName`, `ReceivedAtUtc` and `Status`. Blank optional text is already null. The
`AppVersion` and `Platform` values are as the app sent them, not trimmed. `Status` is always `FeedbackStatus.New` from the manager.

### `FeedbackListQuery`

`Page` (a `PageRequest` from Ballast.Paging), `Category` (null for every category) and `Status` (null for every status).

### `FeedbackManager`

```csharp
public FeedbackManager(IFeedbackStore store, ILogger<FeedbackManager> logger, FeedbackOptions? options = null, TimeProvider? timeProvider = null)
```

| Parameter | Default | Meaning |
|---|---|---|
| `store` | (required) | Where the messages are kept. |
| `logger` | (required) | Where a stored message is logged, at Information, with its id and category. |
| `options` | the defaults | See above. |
| `timeProvider` | `TimeProvider.System` | The clock that stamps the receipt time. The receipt is `GetUtcNow().UtcDateTime`. |

**`SubmitAsync(FeedbackRequest request, CancellationToken ct = default)`** checks the request, trims its text (blank text becomes
null, and the message is trimmed), saves it as `New`, and returns the saved `FeedbackResponse` with its id and its receipt time in UTC.

**`ListAsync(int page, int perPage, string? category, FeedbackStatus? status, CancellationToken ct = default)`** returns one
page, newest first. A blank `category` is no filter. The upper limit on `perPage` (500 in the apps) is the caller's to check.

**`SetStatusAsync(int id, FeedbackStatus status, CancellationToken ct = default)`** sets a status. It returns `false` for an id
with no row.

Ways each fails:

| Member | Situation | Result |
|---|---|---|
| constructor | `store`, `logger` or `options` is null (where required) | `ArgumentNullException` |
| constructor | an option is blank | `ArgumentException` |
| `SubmitAsync` | `request` is null | `ArgumentNullException` |
| `SubmitAsync` | the message is blank | `AppException`, 400, `InvalidCode`, detail `message is required.`. Nothing is saved. |
| `SubmitAsync` | a value is over its cap | `AppException`, 400, `InvalidCode`, detail names the field and its cap, for example `message must be 4000 characters or fewer.`. Nothing is saved. |
| `SubmitAsync` | the reply address is not an address | `AppException`, 400, `InvalidCode`, detail `email must be an e-mail address.`. Nothing is saved. |
| `SubmitAsync` | `Categories` is set and the category is not in it | `AppException`, 400, `UnknownCategoryCode`, detail lists the allowed categories. Nothing is saved. |
| `SubmitAsync` | the store throws | The exception propagates. The manager reports no success. |
| `ListAsync` | `page` or `perPage` is below 1 | `ArgumentOutOfRangeException`, before the store is asked |
| `ListAsync` | the store throws | The exception propagates. |
| `SetStatusAsync` | `status` is not one of the four | `AppException`, 400, `InvalidCode`. The store is not asked. |
| `SetStatusAsync` | the store throws | The exception propagates. |

The manager does not write the endpoint's answer. `SubmitAsync` returns the saved row, and the app's endpoint answers 204
(no content), as the apps' endpoint always has.

## Client: `FeedbackClient`

```csharp
public FeedbackClient(HttpClient http, ILogger<FeedbackClient>? logger = null, FeedbackOptions? options = null)
```

The `HttpClient`'s base address is the API's root. The app supplies it, so no address ships in the library.

**`SendAsync(FeedbackRequest request, CancellationToken ct = default)`** returns a `FeedbackSendResult`:

1. A message that is blank after trimming is not posted. The result is `Empty`, with code `FeedbackEmpty`.
2. Otherwise the client builds a copy of the request: the message, subject, reply address, category and context name are trimmed,
   and blank ones are sent as null. The app version and platform are sent as the app gave them. The request is never changed.
3. It posts the copy as JSON to `Route`, using the source-generated serializer.

| Answer | Outcome | Code |
|---|---|---|
| Any 2xx (the server answers 204) | `Sent` | empty |
| 501 | `NotConfigured` | the server's code, or `FeedbackNotConfigured` |
| 404 | `NotAvailable` | the server's code, or `FeedbackNotAvailable` |
| Any other status | `Rejected` | the server's code, or `FeedbackRejected` |
| No connection, or a timeout (`HttpRequestException` or `TaskCanceledException`) | `Unreachable` | `FeedbackUnreachable` |
| Blank message | `Empty` | `FeedbackEmpty` |

The server's problem fields (`ServerTitle`, `ServerDetail`, `TraceId`) are set when the answer carried them, and are null
otherwise. The library names no English word: the app shows its own words for each outcome, and uses `ServerTitle` only as
the server's own text when it has one.

A send that fails at the HTTP call is reported as an outcome, not thrown. A cancelled `ct` that cancels the HTTP call surfaces as a
`TaskCanceledException`, which is reported as `Unreachable`, as the apps have always done. A body that is not JSON gives no
problem fields, and the outcome is still the one the status gives. An exception the client does not expect is not caught, and
propagates.

Ways the constructor fails: a null `http` throws `ArgumentNullException`; a blank `Route` throws `ArgumentException`.

`SendAsync` throws `ArgumentNullException` for a null request.

## Results

### `FeedbackSendOutcome`

`Sent`, `Empty`, `NotConfigured`, `NotAvailable`, `Rejected`, `Unreachable`.

### `FeedbackSendResult`

`Outcome`, `Code`, and the optional `ServerTitle`, `ServerDetail` and `TraceId`. `Code` is the server's code when it sent one,
otherwise the fallback in `FeedbackCodes` for the outcome. A `Sent` result has an empty `Code`.

## What the app does, and what the library does not

- The endpoint: a `[AllowAnonymous]` controller action that calls `FeedbackManager.SubmitAsync` and answers 204. The rate limit
  and the authorization for list and status are the app's.
- The store: an `IFeedbackStore` over the app's database, including any column for a review mark, which `SetStatusAsync` clears.
- The categories' labels and their routing (for example, which categories go to a different destination). `Categories` only
  says which names are allowed.
- The app version and platform, which come from the app's own build information, and the device facts.
- The privacy scrub of a deleted account's messages.
- The words the user sees. The library returns outcomes and codes, never sentences.

## Notes on the wire

- The request and response use the JSON names above, in camelCase, as the web defaults write them. A null optional value is
  written as `null`, as it always was.
- The client writes its request with a source-generated serializer, so the library works in a trimmed or AOT-compiled app.
- `receivedAt` is written as a `DateTime` in UTC. The store must return it with `DateTimeKind.Utc`, or the JSON carries no `Z`.
