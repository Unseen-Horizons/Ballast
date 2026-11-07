# Ballast usage guides

Each library has a usage guide. It covers every option and every way the library fails. The package README of each library
has a short example that runs as written.

- [Ballast.Errors](Errors.md): error type, codes, problem answer and problem reader.
- [Ballast.Paging](Paging.md): page request, offset and paged result.
- [Ballast.Time](Time.md): time zone lookup, instants, place time, the local clock and public holiday calendars for registered countries.
- [Ballast.Text](Text.md): text folding and the personal-data masker.
- [Ballast.Statistics](Statistics.md): the normal upper tail, a pooled two-proportion z, Benjamini-Hochberg adjustment, and the median and median absolute deviation.
- [Ballast.Geo](Geo.md): coordinate checks, distance, bounding boxes and polygons.
- [Ballast.Money](Money.md): currency codes, rounding, totals and formatting.
- [Ballast.Translations](Translations.md): parity checks for .resx and JSON translations, and a scan for the keys source code asks for.
- [Ballast.Email](Email.md): SendGrid sending, the send rules, the address check and the send-once ledger. Setup: [SendGrid](setup/sendgrid.md).
- [Ballast.Storage](Storage.md): atomic writes, leftover sweep, safe relative paths, the data folder, file types and reads capped at a declared size.
- [Ballast.AspNetCore](AspNetCore.md): problem answers, exception mappings, rate limits, security headers, startup checks, the credential protector and forwarded headers.
- [Ballast.Health](Health.md): readiness and liveness routes, build info, database and schema checks, startup gates, warm-up and the startup hold.
- [Ballast.Http](Http.md): public-address guard, redirect rules, capped bodies, host credentials, safe log URLs, Retry-After, the polite fetcher and the per-host pacer.
- [Ballast.SqlServer](SqlServer.md): SQL error classes, the enum and delete conventions, transactions, claims, sequences, batches, collation and connection checks.
- [Ballast.Jobs](Jobs.md): the job lock, leader-only hosted services, the 409 for admin actions and the job-lock readiness check.
- [Ballast.AppAttestation](AppAttestation.md): Play Integrity app sessions, the gate, the web ticket and its cookie, and the store seams. Setup: [Play Integrity](setup/play-integrity.md).
- [Ballast.Feedback](Feedback.md): the feedback request with its caps, the manager over a store, and the client that sends it.
- [Ballast.AppUpdates](AppUpdates.md): the build check: the version answer and its client, the update rules, and the store and install-channel contracts.
- [Ballast.Maui](Maui.md): sealed on-device storage, connectivity state, HTTP client setup, the AdMob banner and native cards, the localisation runtime, crash reports, permission asks, the store review prompt and view helpers.
- [Ballast.Admin](Admin.md): the staff console kit, server and browser: its content security policy and headers, the "who am I" answer, the sign-in state, the page shell, the sign-in page and the staff-and-roles page.
- [@ballast/web](Web.md): the browser helpers for pages (escaping, the instant of a server string, problem answers, the translation loader, place and event times) and the vendor-copy script.
- [Ballast.Auth](Auth.md): sign-in for a web app: rotating refresh tokens, short access tokens with an instant cut-off, lockout, single-use e-mail sign-in links, Google and Microsoft staff sign-in, and access tokens for accounts outside Identity. Setup: [Microsoft](setup/microsoft-sign-in.md), [Google](setup/google-sign-in.md).
- [Ballast.Testing](Testing.md): SQL Server test databases shared by several runs, captured logs, scripted HTTP fakes, test hosts and the source guard kit.
- [Ballast.Build](Build.md): the shared analyzer and nullable settings, the analyzer severities, the NuGet audit and the build-time stamp.

Tooling, not libraries:

- [Deploy](Deploy.md): the PowerShell 7 deploy module, with its configuration, exit codes, migration guard and forbidden settings.
- [CI](CI.md): the reusable .NET workflow, its two wait actions, the SQL Server service in Ballast's CI and the Semgrep rules.
