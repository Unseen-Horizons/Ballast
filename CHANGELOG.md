# Changelog

Every library in Ballast carries the same version. Each shared version has one section here, newest first.

## 0.1.0-preview.1

Not yet published. The first preview: fourteen libraries, all new.

### Added

- **Ballast.Errors**: an error type with stable codes, the problem answer shape, and a reader for problem answers.
- **Ballast.Paging**: a page request with safe limits, an overflow-safe offset, and the paged result shape.
- **Ballast.Time**: time zone lookup, place time, and a local clock that stays in one zone.
- **Ballast.Text**: accent-free text folding and a personal-data masker.
- **Ballast.Geo**: coordinate checks, distance, bounding boxes and polygon containment.
- **Ballast.Money**: currency codes, rounding to minor units, line totals and money formatting.
- **Ballast.Translations**: parity checks for .resx and JSON translations (the same keys, no blank text, the same placeholders, a same-text allow-list) and a scan for the keys source code asks for.
- **Ballast.Email**: e-mail through SendGrid's web API without an SDK, the send rules and address shape check, and a send-once ledger over an app's store.
- **Ballast.Storage**: atomic file writes (overwrite or create-new), removal of leftover temporary files, safe relative paths with a reason for each refusal, the data folder check, and file types read from the first bytes.
- **Ballast.AspNetCore**: problem answers with a stable code and trace reference, exception mappings, a 404 for unknown API routes, fixed-window rate limits, security headers, startup secret checks and forwarded headers from a proxy list.
- **Ballast.Health**: readiness and liveness routes, build info, database and schema checks, startup gates, warm-up and a startup hold.
- **Ballast.Http**: outbound HTTP safety: the public-address guard, one redirect rule set, capped bodies, per-host credentials, safe URLs for logs, Retry-After resilience, and a polite fetcher.
- **Ballast.SqlServer**: SQL error classifiers that look through the whole exception chain, an enum-to-text and restrict-deletes model convention, a retrying transaction helper, a claim helper, a sequence read, a `GO` batch splitter, an isolation-level reset interceptor, a collation check and a developer-database check for connection strings.
- **Ballast.Jobs**: a SQL Server application lock held on its own connection, so only one copy of an app runs its background writes during a deploy; leader-only hosted services, a 409 for admin actions a non-holding copy refuses, and a readiness check for the lock.
- **Ballast.Feedback**: the feedback request with its length caps, a manager over an app's store that checks and saves a message and reads it back for triage, and a client that posts it and reports one of six outcomes.
- **Ballast.AppUpdates**: the build check: the version answer read from the `AppVersion` configuration section and its manager, the client that asks it (404 means no minimum), the pure update rules, the store contract, and the install channel names.
