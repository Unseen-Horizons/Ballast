# Ballast.Email: usage guide

Ballast.Email sends one e-mail through SendGrid's web API. It also says what a send means for the configuration, checks an
address's shape, and keeps a send-once ledger that records each mail so it goes out at most once. It depends on .NET and
on the Microsoft.Extensions packages. It has no SendGrid SDK: the request is written here, as JSON, and sent with an
`HttpClient`.

The package README has the install line and a runnable example. This guide covers every type and member, the
configuration each one reads, and every way each one fails. The setup of the SendGrid account, the sender address and the
key is in `docs/setup/sendgrid.md`.

## How the pieces fit

1. `MailSettings` reads the configuration section: the API key, the sender, and whether to log mail instead of sending it.
2. `AddBallastEmail` registers the settings, the `EmailService`, and, when a key is set, the SendGrid transport.
3. `EmailService` decides what a send means. With a key it asks the transport to send, and it reports the outcome as an
   `EmailSendResult`. Without one it logs the mail or reports `NotConfigured`.
4. `SendOnceLedger` sits over the app's store. It records each e-mail before it is sent, and sends at most once for each
   subject and kind, under the rules in `SendOnceOptions`.

The app keeps its templates. Ballast.Email gets a subject, a text body and an optional HTML body, and sends them.

## Registration

```csharp
public static IServiceCollection AddBallastEmail(
    this IServiceCollection services,
    IConfiguration configuration,
    string sectionName = MailSettings.DefaultSectionName,
    string? defaultFromName = null,
    Action<IHttpClientBuilder>? configureHttpClient = null)
```

| Parameter | Default | Meaning |
|---|---|---|
| `configuration` | (required) | The configuration to read the settings from. |
| `sectionName` | `SendGrid` | The section to read. A different section changes the setting names in every log line and error. |
| `defaultFromName` | null | The sender's display name when the section has none. Null means the e-mail has no sender name. |
| `configureHttpClient` | null | Changes the client that sends the mail, for example its timeout or its message handler. Runs after the default. |

What it registers:

- `MailSettings` as a singleton.
- `EmailService` as a scoped service. Its logging-instead-of-sending default comes from `IHostEnvironment`: on in
  Development, off elsewhere. Without an `IHostEnvironment` in the container the default is off.
- `IMailTransport`, implemented by `SendGridMailTransport` on a typed `HttpClient`, only when an API key is set. Its timeout
  is 30 seconds, and `configureHttpClient` can change it.

It does not register `SendOnceLedger`, because the ledger needs the app's store. Register the ledger beside this call:

```csharp
services.AddScoped(sp => new SendOnceLedger(
    sp.GetRequiredService<MyNotificationStore>(),
    sp.GetRequiredService<EmailService>(),
    new SendOnceOptions { RetryFailed = false }));
```

Ways it fails:

| Situation | Result |
|---|---|
| `services` or `configuration` is null | `ArgumentNullException` |
| The settings are inconsistent (see `MailSettings.Read`) | `InvalidOperationException` at the call, so the app does not start |

The check runs when `AddBallastEmail` is called, not when the first mail is sent.

## MailSettings

```csharp
public sealed class MailSettings
```

The settings. A blank value counts as unset, as an appsettings file ships its values empty.

### Configuration keys

Within the section (`SendGrid` by default):

| Key | Required | Default | Meaning |
|---|---|---|---|
| `ApiKey` | no | unset | The SendGrid API key. Setting it switches sending on. Keep it out of committed files; see `docs/setup/sendgrid.md`. |
| `FromEmail` | yes, when `ApiKey` is set | unset | The sender address. It must be verified with SendGrid. |
| `FromName` | no | the `defaultFromName` argument, else none | The sender's display name. |
| `LogInsteadOfSending` | no | unset: true in Development, false elsewhere | Whether to write the mail to the log when sending is off. Accepts `true` or `false`. |

### Constructor and members

```csharp
public MailSettings(string? apiKey, string? fromEmail, string? fromName = null, string sectionName = "SendGrid", bool? logInsteadOfSending = null)
```

The constructor checks nothing. It trims the values and turns blanks into null.

| Member | Type | Meaning |
|---|---|---|
| `DefaultSectionName` | `string` (const) | `"SendGrid"`. |
| `SectionName` | `string` | The section the values came from. It prefixes the setting names in messages. |
| `ApiKey` | `string?` | The key, or null. |
| `FromEmail` | `string?` | The sender address, or null. |
| `FromName` | `string?` | The sender's name, or null. |
| `LogInsteadOfSending` | `bool?` | The logging option, or null for the environment's default. |
| `IsConfigured` | `bool` | True when an API key is set. |
| `MissingSettings` | `IReadOnlyList<string>` | `{Section}:FromEmail` when the key is set and the sender is not. Empty otherwise. |

### Read

```csharp
public static MailSettings Read(IConfiguration configuration, string sectionName = "SendGrid", string? defaultFromName = null)
```

Reads the section and checks it.

Ways it fails:

| Input | Exception | Message names |
|---|---|---|
| `configuration` is null | `ArgumentNullException` | |
| `sectionName` is null | `ArgumentNullException` | |
| `sectionName` is empty or blank | `ArgumentException` | |
| `ApiKey` is set and `FromEmail` is not | `InvalidOperationException` | `{Section}:FromEmail` and `{Section}:ApiKey` |
| `LogInsteadOfSending` is set to something other than `true` or `false` | `InvalidOperationException` | `{Section}:LogInsteadOfSending` |

A key without a sender is refused here, not at the first send, because every send would otherwise fail the same way.

## SendGridMailTransport

```csharp
public sealed class SendGridMailTransport : IMailTransport
public const string Endpoint = "https://api.sendgrid.com/v3/mail/send";
public SendGridMailTransport(HttpClient http, MailSettings settings, ILogger<SendGridMailTransport> logger)
```

Sends one e-mail with `POST` to `Endpoint`, with `Authorization: Bearer <key>` and `Content-Type: application/json`. The
client is the one the caller gives; the transport does not dispose it. The transport does not retry: a refusal, a timeout
and a connection failure are each one send, and whether to send again is the ledger's decision.

### The request

For `to = "klijent@example.test"`, `subject = "Vaša narudžba"`, `text = "Hello"`, a sender `info@example.test` named
`Example`, and no HTML part, the body is this JSON (on one line in the request):

```json
{
  "from": { "email": "info@example.test", "name": "Example" },
  "subject": "Vaša narudžba",
  "personalizations": [ { "to": [ { "email": "klijent@example.test" } ] } ],
  "content": [ { "type": "text/plain", "value": "Hello" } ],
  "tracking_settings": {
    "click_tracking": { "enable": false, "enable_text": false },
    "open_tracking": { "enable": false }
  }
}
```

- Tracking is off for clicks, in the text too, and for opens. Tracking would rewrite the links in a sign-in or a
  confirmation mail through SendGrid's redirect domain.
- The `name` member is left out when the sender has no name.
- With an HTML body, `content` has a second item, `{"type":"text/html","value":...}`, after the plain-text item. SendGrid
  requires the plain-text part first.
- Non-ASCII text is written as UTF-8, not as `\u` escapes.

### Results and failures

| Situation | Result | What is logged |
|---|---|---|
| SendGrid answers 2xx (it answers 202) | `Sent` | Nothing. |
| SendGrid answers any other status, with its `errors` array | `Failed` | Error: the status, the masked address, and each message. A copy of the address inside a message is masked too. |
| Any other status, and the body is not SendGrid's JSON | `Failed` | Error: the status and the masked address. The body is not logged. |
| The connection fails, or the client times out | `Failed` | Error: the exception, and the masked address. |
| `ct` is cancelled before the call | throws `OperationCanceledException` | Nothing. No request is sent. |
| `ct` is cancelled during the call | throws `OperationCanceledException` | Nothing. |
| `to` is null | throws `ArgumentNullException` | Nothing. A programming error. |
| `to` is empty or blank | throws `ArgumentException` | Nothing. A programming error. |
| `subject` or `text` is null | throws `ArgumentNullException` | Nothing. A programming error. |

A client timeout is a failed send, not a cancellation: the transport rethrows only the caller's own token. The
`SendAsync` call does not let an exception escape for a provider or network problem.

The transport never logs the subject, the text, the HTML, the key or the full address. The log holds the address as
`EmailAddress.Mask` writes it.

### Constructor failures

| Input | Exception |
|---|---|
| A parameter is null | `ArgumentNullException` |
| `settings.ApiKey` is not set | `ArgumentException`, naming `{Section}:ApiKey` |
| `settings.FromEmail` is not set | `ArgumentException`, naming `{Section}:FromEmail` |

## EmailService

```csharp
public class EmailService
public EmailService(MailSettings settings, ILogger<EmailService> logger, IMailTransport? transport = null, bool isDevelopment = false)
public virtual Task<EmailSendResult> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default)
```

Decides what a send means, and asks the transport to send when sending is on.

| Parameter | Meaning |
|---|---|
| `settings` | The settings. |
| `logger` | The logger for every outcome that is not a plain success. |
| `transport` | The transport, or null when none is registered. |
| `isDevelopment` | The default for logging mail instead of sending it, used when `LogInsteadOfSending` is unset. |

### What a send does

The decisions run in this order, and the first that applies is the result:

1. **No API key.** When logging is on, the mail is written to the log at information level, with the masked address,
   the subject and the text (the HTML part is not logged), and the result is `Logged`. When logging is off, a warning names
   `{Section}:ApiKey`, and the result is `NotConfigured`.
2. **A key, but the sender is missing.** An error names `{Section}:FromEmail`, and the result is `NotConfigured`. Nothing is
   sent. (`MailSettings.Read` refuses this combination at startup, so the app does not reach this unless it builds the
   settings itself.)
3. **A key, but no transport.** An error says no transport is registered, and the result is `NotConfigured`.
4. **A key and a transport.** The transport's result is the result. If the transport throws, the exception is logged with
   the masked address and the result is `Failed`.

### Results

| `EmailSendResult` | Meaning | `IsDelivered()` |
|---|---|---|
| `Sent` | The provider accepted the e-mail. | true |
| `Logged` | Sending is off and logging is on: the mail is in the log. | true |
| `NotConfigured` | Sending is off and logging is off, or the settings are incomplete, or no transport is registered. Nothing was sent. | false |
| `Failed` | The provider refused the e-mail, or the call failed. Nothing is known to have been delivered. | false |

`EmailSendResult` carries no details. The details of a failure (the status, the provider's messages) go to the log, from
the transport.

### Failures

| Situation | Result |
|---|---|
| `to` is null | throws `ArgumentNullException` |
| `to` is empty or blank | throws `ArgumentException` |
| `subject` or `text` is null | throws `ArgumentNullException` |
| `ct` is cancelled during the send | throws `OperationCanceledException` |
| The transport throws any other exception, or the call times out | `Failed`, logged |

The service rethrows a cancellation only when the caller's own token is cancelled. A cancellation that comes from inside the
transport is a failure, as a timeout is.

## EmailAddress

```csharp
public static class EmailAddress
public const int DefaultMaxLength = 255;
public static bool IsPlausible(string? address, int maxLength = 255)
public static string Mask(string address)
```

### IsPlausible

A shape check for a form, not a mailbox check. An address passes when:

- it is no longer than `maxLength` characters,
- it is a plain `name@domain`, with no display name, no angle brackets and no spaces,
- the domain has a dot in it, which does not start or end the domain, and has no `..` and no `[`,
- it is already trimmed, so a leading or trailing space fails.

| Input | Result |
|---|---|
| `name@example.com`, `a@b.c`, `UPPER@Example.COM`, `o'brien@example.ie` | `true` |
| null, empty, blank, `plainaddress`, `name@domain`, `name@domain.`, `name@.hr`, `name@dom..hr` | `false` |
| `Name <name@domain.hr>`, `name@domain.hr, other@domain.hr`, ` name@domain.hr` | `false` |
| An address one character longer than `maxLength` | `false` |

| Failure | Exception |
|---|---|
| `maxLength` is below 1 | `ArgumentOutOfRangeException` |

`maxLength` is the cap. The default, 255, is the usual size of an e-mail column. An app whose column is shorter passes its own
size.

### Mask

Returns the address for a log: the first character of the name, `***`, and the domain. `klijent@example.test` becomes
`k***@example.test`. A single-character name becomes `a***@b.hr`, and an address with no name, such as `@example.test`,
becomes `***@example.test`. A value with no `@` becomes `***`, so none of it shows.

| Failure | Exception |
|---|---|
| `address` is null | `ArgumentNullException` |

## Logging

| Log line | Level | Holds |
|---|---|---|
| Mock mail (logging on) | Information | Masked address, subject, text. Not the HTML part. |
| Not configured (logging off) | Warning | Masked address and the setting name. |
| Settings incomplete, or no transport | Error | Masked address and the setting name. |
| SendGrid refused the mail | Error | Status, masked address, SendGrid's messages. |
| The send failed with an exception | Error | Masked address and the exception. |

The subject and the text are logged in one case only: the mock mail, which is written when logging is on. The HTML part is
never logged. Logging is on by default only in Development. Set `LogInsteadOfSending` to `false` to keep the text out of the
log even there.

The key is never logged, and nothing in the settings is logged.

## SendOnceLedger

```csharp
public sealed class SendOnceLedger
public SendOnceLedger(ISendOnceStore store, EmailService email, SendOnceOptions? options = null, TimeProvider? time = null)
public Task<SendOnceResult> SendOnceAsync(string subjectKey, string kind, string? address, Func<CancellationToken, Task<EmailContent>> compose, CancellationToken ct = default)
```

Sends each kind of e-mail to each subject at most once. A subject is anything the app names with a string, such as its
record's id. A kind is the app's name for a type of e-mail, such as `reminder-7`. The pair (subject, kind) is the key.

### The flow

For one call:

1. **Check.** `FindAsync` reads the record for the pair.
2. **Decide.** The record decides, as the table below shows. A missing address reserves a `NoAddress` record and stops.
3. **Compose.** `compose` builds the e-mail. This runs before anything is written, so a composition that fails leaves the
   record as it was.
4. **Reserve or claim.** A new pair is reserved as `Pending`. An existing record that may be retried is claimed as `Pending`.
   Either write is conditional: when another sender got there first, the call stops with `InProgress`.
5. **Send.** `EmailService.SendAsync` sends the e-mail.
6. **Mark.** The record becomes `Sent` when the result is delivered (`Sent` or `Logged`), otherwise `Failed`.

A reservation comes before the send, so a crash between steps 4 and 5 is a mail that is not sent. It is never two.

### The record and its states

| `SendOnceState` | Written when | Final? |
|---|---|---|
| `Pending` | Reserved or claimed, before the send. Stays so when the send is cancelled, its mark fails, or the process dies. | No: see `PendingAfter`. |
| `Sent` | The provider accepted the e-mail, or it was logged. | Yes. |
| `NoAddress` | The subject has no address. Nothing is sent. | Yes. |
| `Failed` | The send did not deliver. | Only while `RetryFailed` is off or the record is older than `FailedRetryWindow`. |

### How a call decides

| Record found | Address given | Result |
|---|---|---|
| None | No | Reserve `NoAddress`. Outcome `NoAddress`. |
| None | Yes | Compose, reserve `Pending`, send, mark. Outcome `Sent` or `Failed`. If the reservation loses, `InProgress`. |
| `Sent` or `NoAddress` | Any | `AlreadyRecorded`. |
| `Failed`, within the window, `RetryFailed` on | Yes | Compose, claim `Failed` to `Pending`, send, mark. Outcome `Sent` or `Failed`. If the claim loses, `InProgress`. |
| `Failed`, older than the window, or `RetryFailed` off | Any | `AlreadyFailed`. |
| `Failed` | No | `AlreadyFailed`. |
| `Pending`, not older than `PendingAfter` (or `PendingAfter` unset) | Any | `InProgress`. |
| `Pending`, older than `PendingAfter` | Yes | Compose, claim `Pending` to `Pending` with the current time, send, mark. Outcome `Sent` or `Failed`. |
| `Pending` | No | `InProgress`. |

### Outcomes

| `SendOnceOutcome` | Meaning | `SendOnceResult.Delivery` |
|---|---|---|
| `Sent` | This call sent the e-mail, and it was delivered. | `Sent` or `Logged` |
| `Failed` | This call sent the e-mail, and it was not delivered. The record is `Failed`. | `NotConfigured` or `Failed` |
| `NoAddress` | This call recorded that the subject has no address. | null |
| `AlreadyRecorded` | A `Sent` or `NoAddress` record already exists. | null |
| `AlreadyFailed` | A final `Failed` record exists. | null |
| `InProgress` | A `Pending` record that is not stale exists, or another sender won the reservation or the claim. | null |

### Options

```csharp
public sealed class SendOnceOptions
```

| Property | Type | Default | Meaning |
|---|---|---|---|
| `RetryFailed` | `bool` | `true` | Whether a `Failed` record may be claimed and sent again. With `false`, a `Failed` record is final. |
| `FailedRetryWindow` | `TimeSpan` | 48 hours | How long after its first reservation a `Failed` record may still be retried. Once older, the failure is final. Must be positive. Ignored when `RetryFailed` is `false`. |
| `PendingAfter` | `TimeSpan?` | null (never) | The age after which a `Pending` record may be claimed again. Null means a `Pending` record is never reclaimed. |

`Validate()` checks the options, and the constructor calls it:

| Input | Exception |
|---|---|
| `FailedRetryWindow` is zero or negative | `ArgumentOutOfRangeException`, naming `FailedRetryWindow` |
| `PendingAfter` is set and zero or negative | `ArgumentOutOfRangeException`, naming `PendingAfter` |
| `RetryFailed` is on, and `PendingAfter` is shorter than `FailedRetryWindow` plus one hour | `ArgumentOutOfRangeException`, naming `PendingAfter` |

**Why the last rule.** A retried `Failed` record keeps its first time, so the window can end. So a retry that is still sending
can look older than `PendingAfter`, when `PendingAfter` is short. Another sender could then claim it while the first send is
still running, and the mail would go out twice. A margin of one hour, which is far longer than a send takes, closes that.
With `RetryFailed` on, the earliest useful `PendingAfter` is therefore 49 hours by default. For crash recovery in minutes, turn
`RetryFailed` off.

### The time of a record

`CreatedAtUtc` on a record has two meanings, and both are needed for the rules above:

- A `Failed` record keeps its first reservation time. The retry window counts from it, so a permanently failing address becomes
  final.
- A `Pending` record takes the time its current attempt was claimed. A stale check measures from the claim, so a second
  sender cannot reclaim a send that has just started.

### ISendOnceStore

```csharp
public interface ISendOnceStore
{
    Task<SendOnceRecord?> FindAsync(string subjectKey, string kind, CancellationToken ct = default);
    Task<bool> TryReserveAsync(SendOnceRecord record, CancellationToken ct = default);
    Task<bool> TryClaimAsync(SendOnceClaim claim, CancellationToken ct = default);
    Task MarkAsync(string subjectKey, string kind, SendOnceState state, CancellationToken ct = default);
}
```

The app implements it over its own table. The table needs:

- a unique index on (subject, kind), so a second reservation is refused;
- a state column, which holds the four `SendOnceState` values;
- a recipient column, which the ledger writes;
- a time column, which holds `CreatedAtUtc`.

| Method | Must do |
|---|---|
| `FindAsync` | Return the record, or null. |
| `TryReserveAsync` | Insert the record. Return `false` when the unique index refuses it, which means another sender reserved first. Any other failure throws. |
| `TryClaimAsync` | In one conditional update, set the state to `Pending`, the recipient and the time from the claim, `WHERE` the state and the time still equal the claim's expected values. Return `true` when one row changed. Two claims for one record must not both succeed. |
| `MarkAsync` | Set the state. Throw `InvalidOperationException` when the record does not exist. |

A store that cannot do its work throws. Only `TryReserveAsync` and `TryClaimAsync` return `false`, and only for the reason their
names give.

### Failures

| Situation | Result |
|---|---|
| `subjectKey` or `kind` is null | `ArgumentNullException` |
| `subjectKey` or `kind` is empty or blank | `ArgumentException` |
| `compose` is null | `ArgumentNullException` |
| `FindAsync` throws | The exception propagates. Nothing is written. |
| `compose` throws | The exception propagates. The record is as it was: none is written for a new pair, and a `Failed` record stays `Failed`. |
| `TryReserveAsync` or `TryClaimAsync` throws | The exception propagates. If the write committed before the throw, the record exists and the next call sees it. |
| `TryReserveAsync` or `TryClaimAsync` returns `false` | `InProgress`. Nothing is sent. |
| The send returns `Failed` or `NotConfigured` | The record is marked `Failed`. Outcome `Failed`, with the delivery. |
| The send returns `Sent` or `Logged` | The record is marked `Sent`. Outcome `Sent`. |
| The caller cancels during the send | `OperationCanceledException` propagates. The record stays `Pending`: the mail may or may not have gone out. |
| `MarkAsync` throws | The exception propagates. The record stays `Pending`. |

### Crash and stuck records

A `Pending` record means the outcome is unknown. With `PendingAfter` unset, it blocks its e-mail for ever, and an operator
must look at it. With `PendingAfter` set, a `Pending` record older than that is reclaimed and sent again.

**A crash in the middle of a send, with `PendingAfter` on, sends the mail a second time.** The mail may have gone out before the
crash. That is why `PendingAfter` is off by default.

### InMemorySendOnceStore

```csharp
public sealed class InMemorySendOnceStore : ISendOnceStore
```

Keeps the records in a dictionary behind a lock, and follows the contract exactly, including the compare-and-set in
`TryClaimAsync`. It is for tests and local tools only: its records are lost when the process ends, and they are not shared
between processes.

An app's store is short when its table has the four columns and the unique index above. Each `Try` method is one statement
against the table: the reservation is an insert that the index can refuse, and the claim is an update whose `WHERE` names the
expected state and time. Neither one reads the row first and then writes it, because a read and a write in two steps would
let two senders both win.

## Testing

`InMemorySendOnceStore` and a fake `IMailTransport` are enough to test the rules. For `SendGridMailTransport`, a fake
`HttpMessageHandler` that records the request and returns the answer is enough: no test needs a SendGrid account.
