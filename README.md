# Ballast

Ballast is a set of general building blocks for .NET apps, by Unseen Horizons. Each library is its own NuGet package. The six
basic libraries depend on no other package; the server libraries depend on ASP.NET Core, EF Core and the SQL Server client.

## Libraries

- [Ballast.Errors](src/Ballast.Errors): an error type with stable codes, the problem answer shape, and a reader for problem answers.
- [Ballast.Paging](src/Ballast.Paging): a page request with safe limits, an overflow-safe offset, and the paged result shape.
- [Ballast.Time](src/Ballast.Time): time zone lookup, place time, and a local clock that stays in one zone.
- [Ballast.Text](src/Ballast.Text): accent-free text folding and a personal-data masker.
- [Ballast.Geo](src/Ballast.Geo): coordinate checks, distance, bounding boxes and polygon containment.
- [Ballast.Money](src/Ballast.Money): currency codes, rounding to minor units, line totals and money formatting.
- [Ballast.Statistics](src/Ballast.Statistics): the normal upper tail, a pooled two-proportion z, Benjamini-Hochberg adjustment, and the median and median absolute deviation.
- [Ballast.AspNetCore](src/Ballast.AspNetCore): problem answers with a stable code and trace reference, exception mappings, an API 404, rate limits, security headers, startup secret checks and forwarded headers.
- [Ballast.Health](src/Ballast.Health): readiness and liveness routes, build info, database and schema checks, startup gates, warm-up and the startup hold.
- [Ballast.SqlServer](src/Ballast.SqlServer): SQL error classifiers, the enum and restrict-deletes conventions, a retrying transaction helper, a claim helper, sequences, a GO splitter, an isolation reset, and collation and connection checks.
- [Ballast.Jobs](src/Ballast.Jobs): a SQL Server application lock so one copy of an app runs its background writes, leader-only hosted services, the job-holder 409 and the lock's readiness check.
- [Ballast.Storage](src/Ballast.Storage): atomic file writes, leftover sweep, safe relative paths, the data folder check and file types read from the first bytes.
- [Ballast.Http](src/Ballast.Http): outbound HTTP safety: a public-address guard, one redirect rule set, capped bodies, per-host credentials, safe log URLs, Retry-After resilience and a polite fetcher.
- [Ballast.Email](src/Ballast.Email): e-mail through SendGrid's web API without an SDK, the send rules, an address shape check, and a send-once ledger.
- [Ballast.Translations](src/Ballast.Translations): parity checks for .resx and JSON translations, and a scan for the keys source code asks for.

Each library's usage guide, with every option and every way it fails, is listed in [docs/README.md](docs/README.md).

## Status

Preview. Version 0.1.0-preview.1 is built and packed here. It is not yet published to nuget.org, and the APIs can still
change before 1.0.

## Releases

Only the owner publishes. A release is a version tag, and the owner approves its publish step before any package reaches
nuget.org. See [docs/releasing.md](docs/releasing.md) for the steps, and [docs/setup/publishing.md](docs/setup/publishing.md)
for the one-time setup of the GitHub and nuget.org accounts.

## Licence

Ballast is licensed under the [Mozilla Public License 2.0](LICENSE). If you change Ballast itself and share that change,
you share it under the same licence; your own code stays yours.
