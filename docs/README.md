# Ballast usage guides

Each library has a usage guide. It covers every option and every way the library fails. The package README of each library
has a short example that runs as written.

- [Ballast.Errors](Errors.md): error type, codes, problem answer and problem reader.
- [Ballast.Paging](Paging.md): page request, offset and paged result.
- [Ballast.Time](Time.md): time zone lookup, instants, place time and the local clock.
- [Ballast.Text](Text.md): text folding and the personal-data masker.
- [Ballast.Geo](Geo.md): coordinate checks, distance, bounding boxes and polygons.
- [Ballast.Money](Money.md): currency codes, rounding, totals and formatting.
- [Ballast.Translations](Translations.md): parity checks for .resx and JSON translations, and a scan for the keys source code asks for.
- [Ballast.Email](Email.md): SendGrid sending, the send rules, the address check and the send-once ledger. Setup: [SendGrid](setup/sendgrid.md).
- [Ballast.Storage](Storage.md): atomic writes, leftover sweep, safe relative paths, the data folder and file types.
- [Ballast.AspNetCore](AspNetCore.md): problem answers, exception mappings, rate limits, security headers, startup checks and forwarded headers.
- [Ballast.Health](Health.md): readiness and liveness routes, build info, database and schema checks, startup gates, warm-up and the startup hold.
- [Ballast.Http](Http.md): public-address guard, redirect rules, capped bodies, host credentials, safe log URLs, Retry-After and the polite fetcher.
- [Ballast.SqlServer](SqlServer.md): SQL error classes, the enum and delete conventions, transactions, claims, sequences, batches, collation and connection checks.
- [Ballast.Jobs](Jobs.md): the job lock, leader-only hosted services, the 409 for admin actions and the job-lock readiness check.
- [Ballast.AppAttestation](AppAttestation.md): Play Integrity app sessions, the gate, the web ticket and its cookie, and the store seams. Setup: [Play Integrity](setup/play-integrity.md).
