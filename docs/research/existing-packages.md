# Do the packages we plan already exist?

Research for the owner of Unseen Horizons. Date of reading: **2026-10-31**. Every source below was read that day.

The question: "maybe the ones we offer already exist, and instead of making ours we use those?" This file goes library by
library (all 33) and gives a verdict for each.

## How to read this file

- **Use instead**: an existing package does the job. Ballast should not have this library.
- **Thin layer**: Ballast keeps a small library that configures or wraps existing packages to add the house rules.
- **Keep ours**: nothing established fits. The reason is given.
- "Active" means a release or a default-branch commit between 2025-10-31 and 2026-10-31.
- Version, release date, licence expression and target frameworks come from the nuget.org registration API
  (`api.nuget.org/v3/registration5-gz-semver2/<id>`), read 2026-10-31. npm data comes from `registry.npmjs.org`.
  Repository facts (last commit, archived flag) come from the GitHub API. Where a package carries a licence **file**
  instead of an expression, I read that file on its nuget.org licence page.
- I installed nothing. I ran a few tiny experiments on built-in .NET types (SDK 10.0.401, Windows 11) and say so where I did.
- I read package metadata and docs. I did not read every package's source. Where a claim rests on less than that, it says
  **NEEDS VERIFICATION**.
- Licence remarks are engineering notes, not legal advice.

## Summary

| # | Library | Verdict | Main package or reason |
|---|---|---|---|
| 1 | Ballast.Errors | Keep ours | Must work in the phone app without ASP.NET. Built-ins write the answer but carry no stable code. |
| 2 | Ballast.Time | Keep ours | `TimeZoneInfo` breaks the overlap and gap rules (tested). NodaTime is correct but changes every type. |
| 3 | Ballast.Paging | Keep ours | Nothing gives the app's JSON shape with refusal of over-limit values. |
| 4 | Ballast.Text | Keep ours | No package does the `đ`→`dj` rule or this masker. libphonenumber could refine phones later. |
| 5 | Ballast.Geo | Keep ours | NetTopologySuite is heavier. MAUI's own distance call wraps a bad longitude instead of refusing it. |
| 6 | Ballast.Money | Keep ours | NodaMoney is the real alternative. Different rounding defaults. Closest call of the six. |
| 7 | Ballast.AspNetCore | Thin layer | Built-in problem details, exception handler, rate limiter, validation. NetEscapades.AspNetCore.SecurityHeaders. |
| 8 | Ballast.Health | Thin layer | Microsoft.Extensions.Diagnostics.HealthChecks (+ EF Core check). Build info, schema check and startup hold are ours. |
| 9 | Ballast.Telemetry | **Use instead** | OpenTelemetry + Microsoft's Azure Monitor distro. |
| 10 | Ballast.SqlServer | Thin layer | EF Core retry strategy (covers deadlocks), `ExecuteUpdate`, EntityFrameworkCore.Exceptions.SqlServer. |
| 11 | Ballast.Jobs | Thin layer | DistributedLock.SqlServer (it is `sp_getapplock`). |
| 12 | Ballast.Storage | Thin layer | FileSignatures for sniffing. Atomic write is ours. Avoid Mime-Detective (licence clause). |
| 13 | Ballast.Http | Thin layer | Meziantou SSRF handler (or idunno.Security.Ssrf) + Microsoft.Extensions.Http.Resilience. |
| 14 | Ballast.Email | Thin layer | SendGrid web API, Azure Communication Services, MailKit. Ledger is ours. |
| 15 | Ballast.Excel | **Use instead** | ClosedXML (MIT). Avoid EPPlus and NPOI's binary terms. |
| 16 | Ballast.Translations | Thin layer | resx + Microsoft.Extensions.Localization, i18next. Only the parity tests are ours. |
| 17 | Ballast.Auth | Thin layer | Identity + JwtBearer + Google.Apis.Auth + Microsoft.Identity.Web. Not Duende. OpenIddict is the "real server" option. |
| 18 | Ballast.AppAttestation | Thin layer | Google.Apis.PlayIntegrity.v1 (server) + Xamarin Play Integrity binding (client). |
| 19 | Ballast.Feedback | Keep ours | A table, an endpoint and a form. Sentry feedback goes to a third party. |
| 20 | Ballast.AppUpdates | Thin layer | Play in-app update binding for the Play flow. The server-driven "required build" notice is ours. |
| 21 | Ballast.Admin | Keep ours | UI kits give CSS only. Weakest case for a public package. |
| 22 | Ballast.Maui | Thin layer | CommunityToolkit.Maui / Mvvm, Plugin.AdMob. Publish only what they lack. |
| 23 | @ballast/web | Thin layer | `fetch` or ky for requests, lit-html for escaping. Problem reading is ~50 lines. |
| 24 | @ballast/admin | Keep ours | Tabler / AdminLTE give styling, not your shell. |
| 25 | Ballast.Testing | Thin layer | xUnit v3, Testcontainers, Respawn, ArchUnitNET. Avoid FluentAssertions 8. |
| 26 | Ballast.Build | Thin layer | MinVer or Nerdbank.GitVersioning, Source Link, Meziantou.Analyzer. Ship props, not tools. |
| 27 | Reusable CI workflows | Thin layer | GitHub reusable workflows + `NuGet/login` (trusted publishing). |
| 28 | Semgrep rules | Keep ours | Write your own rules. Do not copy Semgrep's registry rules (licence). |
| 29 | Deploy module | Thin layer | `az webapp deploy` / `Azure/webapps-deploy`. Health proof and tags are ours. |
| 30 | Mooring.Documents | Thin layer | ZXing.Net, PdfPig, MimeKit, Ical.Net, PDFtoImage. The 15-language date reader is ours. |
| 31 | Mooring.Scraping | Keep ours | The engine is yours. Parts: AngleSharp, PdfPig, ClosedXML. Watch JsonPath.Net's binary terms. |
| 32 | Mooring.Ai | Keep ours | Official Anthropic SDK underneath. The job rules are yours. |
| 33 | Mooring.Audit | Thin layer | gitleaks, OSV-Scanner, Trivy, Semgrep/Opengrep as the no-token scan. Claude workflow is ours. |

**Counts: Use instead 2, Thin layer 19, Keep ours 12.**

**Decided (owner, 2026-10-31):** Ballast.Telemetry and Ballast.Excel are dropped. Projects use OpenTelemetry with Microsoft's
Azure Monitor distro, and ClosedXML, directly. Ballast is 27 public libraries; Mooring stays at 4.

For the six basics that are already written: **do not switch any of them before the first publish.** Section 1 to 6 give the
reasons and the one condition under which each would change.

## What matters most

### A. Licence problems found (verified at the source)

The owner's list was a good guess. I verified each name, and found more. "NuGet file" means the licence file inside the
package on nuget.org, which can differ from the repository's licence.

| Package | What the package says (read 2026-10-31) | Source | Consequence |
|---|---|---|---|
| Duende.IdentityServer 8.0.10 | Commercial. Licence file points to Duende's licence. Community Edition is for "qualifying startups and non-profits"; Duende's offer page (read via search) says revenue under USD 1M and capital access under USD 3M, and not for redistribution or software for third parties. A 2022 Duende blog says the limits would end. NEEDS VERIFICATION which is current. | [nuget licence](https://www.nuget.org/packages/Duende.IdentityServer/8.0.10/license), [pricing](https://duendesoftware.com/pricing), [community edition](https://duendesoftware.com/products/communityedition) | Never a dependency of a public Ballast package. |
| EPPlus 8.7.2 | Polyform Noncommercial 1.0.0. Commercial use needs a paid licence. | [nuget licence](https://www.nuget.org/packages/EPPlus/8.7.2/license) | Do not use. ClosedXML does the job. |
| QuestPDF 2026.9.1 | Dual licence. Free Community licence under USD 1,000,000 annual gross revenue; Professional USD 1,999; Enterprise USD 4,999. Its guide counts code written by an AI coding tool as written by a developer of the organisation. | [nuget licence](https://www.nuget.org/packages/QuestPDF/2026.9.1/license), [pricing](https://www.questpdf.com/pricing) | Not needed (it makes PDFs; we read them). Never in Ballast. |
| FluentAssertions 8.11.0 | Xceed Community Licence: non-commercial use only. Commercial use is paid. | [nuget licence](https://www.nuget.org/packages/FluentAssertions/8.11.0/license) | Do not put in Ballast.Testing. Use Shouldly, plain xUnit asserts, or AwesomeAssertions (Apache-2.0 fork). |
| SixLabors.ImageSharp 4.1.3 | Six Labors Split Licence: Apache-2.0 for open-source use, transitive use, for-profit direct use under USD 1M revenue, or non-profits. Otherwise a paid commercial licence. | [nuget licence](https://www.nuget.org/packages/SixLabors.ImageSharp/4.1.3/license) | Avoid as a direct dependency. SkiaSharp (MIT) is already used. |
| MediatR 14.2.0 and AutoMapper 16.2.0 | Reciprocal Public Licence 1.5, or a paid Lucky Penny Software licence. (I read these two versions; I did not check where the change began.) | [MediatR](https://www.nuget.org/packages/MediatR/14.2.0/license), [AutoMapper](https://www.nuget.org/packages/AutoMapper/16.2.0/license) | Do not use. Not needed (hand-written mappers are the house rule). |
| MassTransit 9.2.5 | Commercial agreement "applies only to MassTransit v9 and later". v8 and earlier stay free. No price or free tier stated. | [terms](https://massient.com/license) | Do not use for an outbox. |
| TickerQ 11.5.1 | "Switching to commercial, source-available distribution" from package line 10.5.0. Agreement dated 2026-10-13. | [LICENSING.md](https://github.com/Arcenox-co/TickerQ/blob/main/LICENSING.md) | Do not use. |
| Hangfire.Core 1.8.25 | Multi-licensed: LGPL v3 or commercial. | [LICENSE.md](https://github.com/HangfireIO/Hangfire/blob/main/LICENSE.md) | Not needed. LGPL in a public MPL library burdens consumers. |
| NPOI 2.8.1 | Source is Apache-2.0. The NuGet package carries an "Open Source Maintenance Fee Agreement": the fee applies to users with revenue-generating use and annual gross revenue of USD 10,000 or more. It says the fee is not a licence fee and that self-compiling is allowed. | [nuget licence](https://www.nuget.org/packages/NPOI/2.8.1/license) | Do not depend on it. |
| **JsonPath.Net 3.0.3** (json-everything) | Same Open Source Maintenance Fee Agreement, for "all published libraries" of the project, source MIT. | [nuget licence](https://www.nuget.org/packages/JsonPath.Net/3.0.3/license) | Not in Ballast. For Mooring.Scraping the owner decides. Alternatives: Newtonsoft.Json `SelectTokens` (MIT), Jsonata.Net.Native (MIT). |
| PDFsharp 6.5.0 and iText 9.8.0 | AGPL-3.0 (PDFsharp: "AGPL and Commercial License"; iText: AGPL). | [PDFsharp README](https://github.com/empira/PDFsharp), [iText nuget](https://www.nuget.org/packages/itext) | Never a dependency. |
| Mime-Detective 25.8.1 | "MIT-Based License" with an extra clause: no derivative package may be distributed "through any package management system", and it names nuget.org. | [LICENSE.txt](https://github.com/MediatedCommunications/Mime-Detective/blob/main/LICENSE.txt) | Do not use in or under a package published to nuget.org. Whether a plain dependency is a "derivative" is unclear: ask a lawyer, or avoid. FileSignatures (MIT) is the alternative. |
| TruffleHog | AGPL-3.0 (a tool, not a dependency). | [repo](https://github.com/trufflesecurity/trufflehog) | Run it as a tool at most. Gitleaks (MIT) is simpler. |
| Fizzler 1.3.1, SecurityCodeScan 5.6.7 | LGPL-3.0-or-later. Both stale (2024, 2022). | [Fizzler](https://www.nuget.org/packages/Fizzler), [SCS](https://www.nuget.org/packages/SecurityCodeScan.VS2019) | Avoid. AngleSharp does CSS selectors. |
| Semgrep-maintained rules | Semgrep Rules Licence v1.0: internal business use only; not for a competing product or SaaS. The engine is LGPL-2.1. | [Semgrep licensing](https://docs.semgrep.dev/licensing), [post](https://semgrep.dev/blog/2024/important-updates-to-semgrep-oss) | Write your own rules. Do not copy registry rules into Ballast. |
| CodeQL CLI | Free for open-source code on GitHub.com and for research. Other CI use needs a GitHub Advanced Security licence. | [terms](https://github.com/github/codeql-cli-binaries/blob/main/LICENSE.md) | Fine on the public Ballast repo. The private app repos would need a paid licence. |

Two lessons:

1. **The licence on GitHub is not always the licence of the package.** NPOI and json-everything show MIT or Apache-2.0 in the
   repository and a fee agreement in the NuGet package. The .NET Foundation says the same:
   "do not assume that software obtained through a package manager" is governed only by the repository's licence
   ([statement, 2026-08-19](https://dotnetfoundation.org/news-events/detail/.net-foundation-statement-on-open-source-maintenance-fees)).
   Before each Ballast release, read the licence file inside every dependency's `.nupkg`, not just its GitHub page.
2. **The packages the two apps already reference are clean.** I read the direct package references in the GetThere project
   files and checked each licence: ClosedXML, AngleSharp, PdfPig, ZXing.Net, Ical.Net, MimeKit(Lite), OpenTelemetry,
   Anthropic, Google.Apis.Auth, Google.Apis.PlayIntegrity.v1, CommunityToolkit.*, Plugin.AdMob, SkiaSharp, NetTopologySuite,
   CsvHelper, Docnet.Core, Scalar, Microsoft.OpenApi, Grpc.Tools, Google.Protobuf, Azure.Storage.Blobs, the SQLite packages and
   the Microsoft and Xamarin packages I looked up are MIT, Apache-2.0, BSD-3 or "MS-PL OR Apache-2.0". None is in the table
   above. Not checked: the OpenTelemetry instrumentation packages, `Xamarin.Google.Android.Play.Review` and
   `Xamarin.GoogleAndroid.Libraries.Identity.GoogleId`.

### B. A maintenance flag in the apps (outside Ballast)

`Docnet.Core` 2.6.0 (MIT) is referenced by GetThereAPI. Its last release is 2023-09-04 and its last commit is
2023-09-27 ([repo](https://github.com/GowenGit/docnet)). `PDFtoImage` 5.4.0 (MIT, 2026-08-16, includes `net10.0-android36.0`) is the
maintained replacement to consider ([nuget](https://www.nuget.org/packages/PDFtoImage), [repo](https://github.com/sungaila/PDFtoImage)).

### C. Names and publishing

- **Every planned NuGet id is free today.** I asked `api.nuget.org/v3-flatcontainer/<id>/index.json` for the 24 `Ballast.*`
  ids and 4 `Mooring.*` ids in the plan, plus the bare `Ballast` and `Mooring`. All answered 404 on 2026-10-31. (The CI
  workflows, Semgrep rules and Deploy module are not NuGet packages, and the two npm packages are checked below.)
- **A prefix reservation may be refused.** nuget.org's criteria ask whether the prefix "clearly identifies the reservation owner"
  and tell applicants to "avoid common or generic words" ([ID prefix reservation](https://learn.microsoft.com/en-us/nuget/nuget-org/id-prefix-reservation)).
  "Ballast" is a common word. Apply anyway, but plan for a "no". Unreserved ids can still be published.
- **npm scope `@ballast`**: `@ballast/web` and `@ballast/admin` answer 404, so those packages do not exist. Whether the *scope*
  is free **NEEDS VERIFICATION**: npmjs.com answered 403 to my script. An unscoped `ballast` package exists on npm (last
  changed 2022-06-13). Try creating the org before you write code against the name.
- **Use trusted publishing, not an API key.** nuget.org supports OIDC trusted publishing from GitHub Actions and GitLab. The
  key lives one hour, and the policy is bound to the repository and workflow file. A policy for a private repository is
  active for 7 days until the first publish ([docs](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)).

### D. Where custom code is justified only by a house rule

These need real code whatever you reuse: wall-to-UTC with the earlier instant in an overlap (rule 2); a code on every problem
answer (rule 4); the SQL application lock around writing jobs (rule 5); refusal instead of clamping (rule 1). Built-ins
and the packages below do not do these for you. The sections show where.

---

# The six basics

## 1. Ballast.Errors: Keep ours

Sources read 2026-10-31. House rule involved: 4 (stable codes, trace id, no internal text).

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [ASP.NET Core built-ins](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0): `AddProblemDetails`, `IProblemDetailsService`, `IExceptionHandler`, `CustomizeProblemDetails` | MIT | 10.0.14 (2026-10-25) | yes | Writes the answer. Has no code or exception-with-code type. |
| [Hellang.Middleware.ProblemDetails](https://www.nuget.org/packages/Hellang.Middleware.ProblemDetails) | MIT | 6.5.1 (2022-08-09) | no release in 4 years; last commit 2025-06-17 | Targets netcoreapp3.1 to net6.0. Superseded by the built-ins. |
| [Opw.HttpExceptions.AspNetCore](https://www.nuget.org/packages/Opw.HttpExceptions.AspNetCore) | licence file (not read) | 4.0.3 (2024-04-11) | no | Stale. |
| [Pandatech.ResponseCrafter](https://www.nuget.org/packages/Pandatech.ResponseCrafter) | MIT | 7.1.0 (2026-07-26) | yes | Server only: "Exception-based error handling for ASP.NET Core 8+ with RFC 9457". Whether it has stable codes: not checked. |
| [ErrorOr](https://www.nuget.org/packages/ErrorOr) 2.1.1 (2026-05-15), [FluentResults](https://www.nuget.org/packages/FluentResults) 4.0.0 (2025-06-29), [Ardalis.Result](https://www.nuget.org/packages/Ardalis.Result) 10.1.0 (2024-10-28) | MIT | see left | ErrorOr yes; the other two no release in 12 months | Return-value result types. A different job: no wire shape, no client reader. |

Evidence:
- The Learn page links RFC 7807 only, not RFC 9457. Its sample non-development response is generic (title, status, `traceId`).
  The page does not say exception text is hidden for you: the handler you write decides.
- ASP.NET Core 10's `ProblemDetails` is part of the shared framework. A phone app cannot take that framework reference.
  The standalone `Microsoft.AspNetCore.Http.Abstractions` stops at 2.3.13, `netstandard2.0`
  ([nuget](https://www.nuget.org/packages/Microsoft.AspNetCore.Http.Abstractions)). Which assembly a MAUI app could use for
  the type: **NEEDS VERIFICATION**.

**Verdict: Keep ours.** The type must run in the MAUI app, which has no ASP.NET. No package gives `AppException(code,
status)` plus a client-side reader. Ballast.AspNetCore (section 7) should plug `AppException` into the built-in
`IExceptionHandler` and `IProblemDetailsService`, not write its own middleware.

**Switch now? No.** There is nothing to switch to. The overlap with `ProblemDetails` is deliberate.

## 2. Ballast.Time: Keep ours

Sources read 2026-10-31. House rule involved: 2.

I ran this on SDK 10.0.401, Windows 11, `Europe/Zagreb`:

| Call | Result | House rule wants |
|---|---|---|
| `TimeZoneInfo.ConvertTimeToUtc(2026-10-25 02:30)` (overlap) | `01:30Z` (the later instant) | `00:30Z` (the earlier) |
| `TimeZoneInfo.ConvertTimeToUtc(2026-03-29 02:30)` (gap) | throws `ArgumentException` "invalid time" | moved forward to `01:30Z` |
| `TimeZoneInfo.TryConvertWindowsIdToIanaId("Central European Standard Time")` | `Europe/Warsaw` | same as the Ballast doc |

So the built-in conversion breaks the rule in both cases ([docs](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.converttimetoutc)).

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [NodaTime](https://www.nuget.org/packages/NodaTime) | Apache-2.0 | 3.3.7 (2026-10-28) | yes, 11 releases | `Resolvers.LenientResolver`: ambiguous gives the earlier occurrence, a skipped time shifts forward by the gap ([API doc](https://nodatime.org/3.3.x/api/NodaTime.TimeZones.Resolvers.html)). This is exactly the house rule. `IsAotCompatible` on net8.0+ ([csproj](https://github.com/nodatime/nodatime/blob/main/src/NodaTime/NodaTime.csproj)). |
| [TimeProvider](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider) + [FakeTimeProvider](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing) | built-in / MIT | 10.11.0 (2026-10-14) | yes | Already the base of Ballast.Time's clock. |
| [TimeZoneConverter](https://www.nuget.org/packages/TimeZoneConverter) | MIT | 8.0.0 (2026-10-12) | yes | Windows to IANA ids. Built in since .NET 6 (tested above). |
| [GeoTimeZone](https://www.nuget.org/packages/GeoTimeZone) | MIT | 6.1.0 (2025-10-31) | borderline | Zone from coordinates. Not in Ballast.Time's scope. |
| [SimplerSoftware.EntityFrameworkCore.SqlServer.NodaTime](https://www.nuget.org/packages/SimplerSoftware.EntityFrameworkCore.SqlServer.NodaTime) | licence not read | 10.0.1 | yes | Community package. EF Core + SQL Server has no first-party NodaTime support (I found none). |

**Verdict: Keep ours.** The part that matters is about 20 lines and tested. NodaTime is the established library and gets
the rule right, so it is the honest alternative.

**Switch now? No.** NodaTime would put `Instant`, `ZonedDateTime` and friends into every entity, DTO and JSON contract in
both apps and the EF mapping. Ballast.Time lets the apps keep `DateTime` (UTC) and `DateOnly`. Switch only if you decide to
move the whole domain to NodaTime types. Then delete Ballast.Time.

## 3. Ballast.Paging: Keep ours

Sources read 2026-10-31. House rule involved: 1.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [X.PagedList](https://www.nuget.org/packages/X.PagedList) | MIT | 10.6.0 (2026-09-06) | yes | Pages lists. Its result shape is its own. |
| [Gridify](https://www.nuget.org/packages/Gridify) | MIT | 2.20.0 (2026-09-17) | yes | Filtering, sorting and paging by query string. Different scope. |
| [Microsoft.AspNetCore.OData](https://www.nuget.org/packages/Microsoft.AspNetCore.OData) | MIT | 9.5.0 (2026-07-14) | yes | Whole OData protocol (`$top`, `$skip`). Too much. |
| [Sieve](https://www.nuget.org/packages/Sieve) | Apache-2.0 | 2.5.5 (2022-03-30) | no | Stale. |

I read package metadata only. I did not read their docs for how they treat an over-limit page size, so "none refuses with a
field-named error" is a belief, not a finding: **NEEDS VERIFICATION per package.**

**Verdict: Keep ours.** Three small types. The JSON shape (`data, total, page, perPage, totalPages`) is a contract the apps
already read.

**Switch now? No.**

## 4. Ballast.Text: Keep ours

Sources read 2026-10-31. House rule involved: 1.

My experiment (same machine): `"đ".Normalize(FormD)` stays one character. `CompareInfo.Compare("Đakovo", "Dakovo",
IgnoreNonSpace | IgnoreCase)` returned 0 here. So the platform compares `đ` and `d` as equal on this machine. A comparison
is not a stored key, and it depends on ICU or NLS. Whether Android gives the same answer: **NEEDS VERIFICATION.** The `dj`
rule has no built-in form.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [Diacritics](https://www.nuget.org/packages/Diacritics) | MIT | 4.1.8 (2026-04-23) | yes | Removes diacritics. Does not build the comparison key (apostrophes, punctuation, `dj`). |
| [Unidecode.NET](https://www.nuget.org/packages/Unidecode.NET) | MIT | 2.2.1 (2025-09-21) | no release in 12 months | A transliterator. The Ballast.Text guide says Cyrillic must stay Cyrillic. |
| [libphonenumber-csharp](https://www.nuget.org/packages/libphonenumber-csharp) | Apache-2.0 | 9.0.42 (2026-10-27) | yes, 25 releases | Has `FindNumbers` (finds numbers in text). README says trim- and AOT-annotated. |
| [Microsoft.Extensions.Compliance.Redaction](https://www.nuget.org/packages/Microsoft.Extensions.Compliance.Redaction) | MIT | 10.11.0 (2026-10-14) | yes | Built around data classification and redactors. I did not read its API, so I cannot say it detects patterns: **NEEDS VERIFICATION.** |

**Verdict: Keep ours.** The folding rule set is yours. The masker is deliberately broad (seven digits become `[phone]`) and
keeps dates and prices. A phone library would change that behaviour.

**Switch now? No.** One later option: use `FindNumbers` for phones to cut false hits such as `Nalog 1234567`. That is a
behaviour change; do it in a later minor version, not before first publish.

## 5. Ballast.Geo: Keep ours

Sources read 2026-10-31. House rule involved: 1.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [NetTopologySuite](https://www.nuget.org/packages/NetTopologySuite) | BSD-3-Clause | 2.6.0 (2025-03-12) | repo commits to 2026-10-08, no release in 12 months | Full geometry. Already in both APIs. Box-around-a-point with antimeridian split: I did not find it: **NEEDS VERIFICATION.** Trim annotation: **NEEDS VERIFICATION.** |
| MAUI `Location.CalculateDistance` ([source](https://github.com/dotnet/maui/blob/main/src/Essentials/src/Types/Location.shared.cs)) | MIT | .NET MAUI 10.0.120 (2026-10-21) | yes | Kilometres or miles only. The `Location` constructor throws for a bad latitude but its comment says a longitude "will be projected to the interval (-180, 180]": it wraps, it does not refuse. That breaks rule 1 if used as validation. |
| [GeoJSON.Net](https://www.nuget.org/packages/GeoJSON.Net) | MIT | 1.4.1 (2024-07-12) | no | Ring parsing. Stale. |
| [GeographicLib.NET](https://www.nuget.org/packages/GeographicLib.NET) | MIT | 2.3.2 (2024-10-24) | no | Ellipsoid maths. More than needed. |
| [Spatial4n](https://www.nuget.org/packages/Spatial4n) | Apache-2.0 | 0.4.1.1 (2022-02-11) | no | Stale. |

**Verdict: Keep ours.** Small, validation-first, no dependency. Move to NetTopologySuite if you ever need holes,
multipolygons or real spatial operations.

**Switch now? No.**

## 6. Ballast.Money: Keep ours (closest call)

Sources read 2026-10-31. Rule involved: 1 (explicit, never silent).

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [NodaMoney](https://www.nuget.org/packages/NodaMoney) | Apache-2.0 | 2.9.0 (2026-10-10) | yes | Net10 target, `IsAotCompatible` on net8+. README: `Money` "always rounded to the currency minor unit", "executed after every arithmetic operation"; default `MidpointRounding.ToEven` (765.425 EUR gives 765.42; `AwayFromZero` gives 765.43); unknown code throws; custom currencies via `CurrencyInfo.Register`. Does not mention HRK or BGN. Does not cover per-line totals. [README](https://github.com/RemyDuijkeren/NodaMoney) |
| [NMoneys](https://www.nuget.org/packages/NMoneys) | BSD-3-Clause | 7.5.0 (2026-04-22) | yes | `net6.0` target only. |

Differences from the house rules: half-even by default versus half away from zero, and rounding on every operation versus
`Round()` when asked. Whether implicit rounding can be switched off: **NEEDS VERIFICATION.**

**Verdict: Keep ours.** The table is a fixed ISO 4217 snapshot (List One, 2026-09-17) and keeps HRK and BGN on purpose. The
code is small.

**Switch now? No.** Switch only if you need currency conversion or splitting an amount (`Split`) that NodaMoney already has.

---

# Server plumbing

## 7. Ballast.AspNetCore: Thin layer

Sources read 2026-10-31. Rules involved: 1, 4.

| Piece | Existing answer | Licence / latest / active | Ours |
|---|---|---|---|
| Problem-details middleware | Built-in `AddProblemDetails`, `IExceptionHandler`, `UseStatusCodePages` ([docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0)) | MIT, 10.0.14 (2026-10-25), active | The `AppException` to `Problem` handler, the `code` member, no internal text. |
| Trace id | Sample answers carry `traceId`; ASP.NET sets it | built-in | The response header. |
| Validation | .NET 10 minimal API validation: `AddValidation`, answers customisable through `IProblemDetailsService` ([release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0)). [FluentValidation](https://www.nuget.org/packages/FluentValidation) 12.2.0 (Apache-2.0, 2026-10-10) is the community choice. | built-in | The stable code for a validation failure. |
| Rate limiting | Built-in fixed, sliding, token bucket and concurrency limiters ([docs](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)). The docs say counters live in memory per instance, so three instances allow three times the limit. The IP example uses `RemoteIpAddress.ToString()`. I found no IPv6 prefix grouping on the page. | built-in. [AspNetCoreRateLimit](https://www.nuget.org/packages/AspNetCoreRateLimit) 5.0.0 (2022-12-03) is stale. | The IPv6 /64 partition key, the `RATE_LIMITED` problem answer and `Retry-After`. |
| Security headers | [NetEscapades.AspNetCore.SecurityHeaders](https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders) | MIT, 1.3.1 (2025-12-20), repo commit 2026-07-22 | The default policy. |
| | [NWebsec](https://www.nuget.org/packages/NWebsec.AspNetCore.Middleware) | BSD-3, 3.0.0 (2020-01-13), dead | n/a |

**Verdict: Thin layer.** Do not write a problem-details middleware or a limiter. Ship the defaults and the house parts
(code on every error, IPv6 /64, header set). The multi-instance warning matters: the limiter does not span the two copies
running during a deploy.

## 8. Ballast.Health: Thin layer

Sources read 2026-10-31. House rule involved: 5 context (readiness never needs the lock).

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [Microsoft.Extensions.Diagnostics.HealthChecks](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.HealthChecks) | MIT | 10.0.14 (2026-10-25) | yes | Liveness and readiness by tag, custom `IHealthCheck`, response writers. [Docs](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0). |
| [...HealthChecks.EntityFrameworkCore](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore) | MIT | 10.0.14 (2026-10-25) | yes | `AddDbContextCheck`; calls `CanConnectAsync` by default. Both apps already use it. |
| [AspNetCore.HealthChecks.SqlServer](https://www.nuget.org/packages/AspNetCore.HealthChecks.SqlServer) (Xabaril) | Apache-2.0 | 9.0.0 (2024-12-19) | no release in 12 months; repo commit 2026-04-16 | The docs say this family "isn't maintained or supported by Microsoft". Not needed. |
| Azure App Service [health check](https://learn.microsoft.com/en-us/azure/app-service/monitor-instances-health-check) | platform feature | | | Pings a path; the app decides what it means. |

Evidence: the Learn page has no feature that holds the port closed. Its sample reports `Unhealthy` from the readiness
endpoint until a startup task ends, and the app stays reachable.

**Verdict: Thin layer.** Use the built-ins. Ours: the `/health` build info, the "schema up to date" check, startup gates with
a cap, and the startup hold before the port opens.

## 9. Ballast.Telemetry: Use instead

Sources read 2026-10-31.

| Candidate | Licence | Latest (date) | Active |
|---|---|---|---|
| [OpenTelemetry](https://www.nuget.org/packages/OpenTelemetry), `.Extensions.Hosting`, `.Exporter.OpenTelemetryProtocol` | Apache-2.0 | 1.20.1 (2026-10-20) | yes. Already referenced by GetThere's SharedServer. |
| [Microsoft.OpenTelemetry](https://www.nuget.org/packages/Microsoft.OpenTelemetry) (Microsoft's distro) | MIT | 1.2.0 (2026-10-15) | yes |
| [Azure.Monitor.OpenTelemetry.AspNetCore](https://www.nuget.org/packages/Azure.Monitor.OpenTelemetry.AspNetCore) | MIT | 1.6.0 (2026-07-27) | yes |
| [Microsoft.ApplicationInsights.AspNetCore](https://www.nuget.org/packages/Microsoft.ApplicationInsights.AspNetCore) | MIT | 3.2.0 (2026-10-09) | yes |
| [Serilog](https://www.nuget.org/packages/Serilog) 4.4.0, [Serilog.AspNetCore](https://www.nuget.org/packages/Serilog.AspNetCore) 10.0.0 | Apache-2.0 | 2026-07-10, 2025-11-28 | yes |

Evidence: Microsoft's Learn page (updated 2026-10-09) now says: new apps use the **Microsoft OpenTelemetry Distro**
(`UseMicrosoftOpenTelemetry`); users of the Azure Monitor OpenTelemetry Distro "no action needed"; Application Insights 2.x users
upgrade to 3.x; "Don't initialize both distros in the same application"
([docs](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-enable?tabs=aspnetcore)). Trace ids come from
the standard W3C trace context, which covers rule 4.

For the phone: Visual Studio App Center was retired 2025-03-31; its analytics and diagnostics run to the end of March 2027
([notice](https://learn.microsoft.com/en-us/appcenter/retirement)). [Sentry.Maui](https://www.nuget.org/packages/Sentry.Maui)
6.12.1 (MIT, 2026-10-13, targets net10.0-android36.0) is an option.

**Verdict: Use instead.** Put a ten-line snippet in the Ballast.AspNetCore guide. Choose one Azure distro, not both. The
Microsoft distro is new (16 versions on nuget.org, 1.2.0 on 2026-10-15). The Azure Monitor distro is older, and Microsoft
says its users need no action. Either is a sound base.

## 10. Ballast.SqlServer: Thin layer

Sources read 2026-10-31.

| Piece | Existing answer | Notes |
|---|---|---|
| Enums as strings | EF Core `HasConversion<string>()` per property; bulk rules by CLR type with `ConfigureConventions` ([value conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)). I found no one-line "all enums" switch on that page. | A small helper that applies it to every enum is ours. |
| Claim (conditional update) | `ExecuteUpdateAsync` is built in ([docs](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)). | The helper that turns "0 rows" into a result is ours (about 10 lines). |
| Deadlock retry | `EnableRetryOnFailure()` uses `SqlServerRetryingExecutionStrategy`. EF's detector lists SQL error **1205 (deadlock)** as transient ([source](https://github.com/dotnet/efcore/blob/main/src/EFCore.SqlServer/Storage/Internal/SqlServerTransientExceptionDetector.cs)). The docs warn that the strategy rejects user-started transactions unless the whole unit runs inside `CreateExecutionStrategy().ExecuteAsync(...)` ([docs](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)). | A helper for the wrapped-transaction pattern is where Ballast adds value. |
| Sequences | EF Core `HasSequence` and generated values ([docs](https://learn.microsoft.com/en-us/ef/core/modeling/generated-properties)). | |
| Unique-violation detection | [EntityFrameworkCore.Exceptions.SqlServer](https://www.nuget.org/packages/EntityFrameworkCore.Exceptions.SqlServer) 10.0.1 (2026-05-24), Apache-2.0 ([License.md](https://github.com/Giorgi/EntityFramework.Exceptions/blob/master/License.md)), `net10.0`, repo commit 2026-10-16. | **Use it.** GitHub shows "NOASSERTION" only because the file is `License.md`. |

**Verdict: Thin layer.** Two of the five pieces are "use their package". Keep only the convention helper, the claim helper and
the execution-strategy wrapper.

## 11. Ballast.Jobs: Thin layer

Sources read 2026-10-31. Rule involved: 5.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [DistributedLock.SqlServer](https://www.nuget.org/packages/DistributedLock.SqlServer) (umbrella [DistributedLock](https://www.nuget.org/packages/DistributedLock)) | MIT | 1.0.7 (2026-02-14); umbrella 2.8.3 (2026-07-15) | yes; repo commit 2026-07-14 | "Distributed locks based on Microsoft SQL Server application locks", linking `sp_getapplock`. Connection-scoped by default; a keepalive (default 10 minutes) for the Azure SQL governor; `TryAcquire` with timeout (default zero); `HandleLostToken` exists in the changelog. [SQL Server doc](https://github.com/madelson/DistributedLock/blob/master/docs/DistributedLock.SqlServer.md), [README](https://github.com/madelson/DistributedLock). |
| [Quartz](https://www.nuget.org/packages/Quartz) | Apache-2.0 | 4.6.2 (2026-10-26) | yes | A scheduler with a job store. More than needed. |
| [Hangfire.Core](https://www.nuget.org/packages/Hangfire.Core) | LGPL v3 or commercial | 1.8.25 (2026-08-28) | yes | Licence, see section A. |
| [NCronJob](https://www.nuget.org/packages/NCronJob) | MIT | 4.12.3 (2026-09-25) | yes | Cron on `IHostedService`. No leader election. |
| [Cronos](https://www.nuget.org/packages/Cronos) | MIT | 0.13.0 (2026-04-29) | yes | Cron parser only. |
| [Coravel](https://www.nuget.org/packages/Coravel) | MIT | 6.0.2 (2025-01-17) | no | Stale. |
| TickerQ | commercial from 10.5.0 | | | See section A. |

**Verdict: Thin layer on DistributedLock.SqlServer.** The lock is not worth rewriting. Ours: `LeaderHostedService<T>`,
`IJobLeadership.IsHolder`, `[RequireJobHolder]` and the "off outside Production" switch. One check before you drop the
current `sp_getapplock` code: confirm DistributedLock holds the lock on a dedicated connection that survives pool recycling
**(NEEDS VERIFICATION)**; the README's keepalive note suggests it handles this.

## 12. Ballast.Storage: Thin layer

Sources read 2026-10-31. Rule involved: 1.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [FileSignatures](https://www.nuget.org/packages/FileSignatures) | MIT | 7.3.0 (2026-08-14) | yes; targets net10.0, net9.0, net8.0 | Detects file type from the header. |
| [Mime-Detective](https://www.nuget.org/packages/Mime-Detective) | "MIT-Based" with a no-derivative-package clause (section A) | 25.8.1 (2025-08-01) | no release in 12 months | Avoid. |
| Atomic file write | I searched nuget.org for "atomic file write": the hits were an NLog target, a file-provider and unrelated packages. None is a small "write temp, flush, replace" helper. | | | Ours. |

**Verdict: Thin layer.** Atomic writes and the data-root guard are ours (small). File sniffing: use FileSignatures. The app's
own `TicketFileSniffer` (in GetThereShared) was not examined.

## 13. Ballast.Http: Thin layer

Sources read 2026-10-31. Rules involved: 1, 3.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [Meziantou.Framework.Http.ServerSideRequestForgery](https://www.nuget.org/packages/Meziantou.Framework.Http.ServerSideRequestForgery) | MIT | 2.0.8 (2026-10-28) | yes, 12 releases | Scheme allow-list plus IP check at connect time (`SocketsHttpHandler.ConnectCallback`), DNS resolved on each connection "to avoid TOCTOU". Default unsafe: loopback, private, link-local, CGNAT, multicast, reserved, IPv6 transition. Limits: proxies and HTTP/3 are rejected. The README does not describe redirects. [README](https://github.com/meziantou/Meziantou.Framework/tree/main/src/Meziantou.Framework.Http.ServerSideRequestForgery) |
| [idunno.Security.Ssrf](https://www.nuget.org/packages/idunno.Security.Ssrf) | MIT | 5.4.0 (2026-07-14) | yes; repo commit 2026-10-30 | Handler for `HttpClient` and `ClientWebSocket`; `IsUnsafeUri`, `IsUnsafeIpAddress`; extra ranges can be added. The page does not say how DNS rebinding or redirects are handled. [README](https://github.com/blowdart/idunno.Security.Ssrf) |
| [Microsoft.Security.AntiSSRF](https://www.nuget.org/packages/Microsoft.Security.AntiSSRF) | MIT | 1.0.1 (2026-10-08) | yes, only 2 versions | "Robust URL validation" plus an agent that blocks internal addresses. [README](https://github.com/microsoft/AntiSSRF) |
| [Microsoft.Extensions.Http.Resilience](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience) | MIT | 10.11.0 (2026-10-14) | yes | Retries, timeouts, circuit breaker on Polly v8. [Docs](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience). |
| [Polly](https://www.nuget.org/packages/Polly) | BSD-3-Clause | 8.9.0 (2026-10-28) | yes | The engine under it. |

What none of them does (as read): refuse `user:password@` in a URL, check each redirect hop by hand with a hop limit, cap the
body size, or inject per-host basic auth from configuration.

**Verdict: Thin layer.** Compose a connect-time SSRF handler with the resilience handler, then add the house rules. The
connect-time check beats a URL-only check against DNS rebinding. Pick one of Meziantou or idunno (both are single-maintainer
projects); Meziantou had the newest release.

## 14. Ballast.Email: Thin layer

Sources read 2026-10-31.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [SendGrid](https://www.nuget.org/packages/SendGrid) (official) | MIT | 9.29.3 (2024-04-02) | no release since; default branch last commit 2024-04-02 | Works against a stable web API. A plain `HttpClient` call is an option. |
| [Azure.Communication.Email](https://www.nuget.org/packages/Azure.Communication.Email) | MIT | 1.1.0 (2025-10-07) | no release in 12 months | Fits an Azure-hosted app. |
| [MailKit](https://www.nuget.org/packages/MailKit) | MIT | 4.18.2 (2026-10-23) | yes | SMTP. |
| [FluentEmail.Core](https://www.nuget.org/packages/FluentEmail.Core) | MIT | 3.0.2 (2022-03-17) | no | The provider-abstraction package. Last commit 2022-03-21. Stale. |
| [MassTransit](https://www.nuget.org/packages/MassTransit), [WolverineFx](https://www.nuget.org/packages/WolverineFx.SqlServer), [DotNetCore.CAP](https://www.nuget.org/packages/DotNetCore.CAP) | commercial (v9+) / MIT / MIT | 9.2.5 / 6.60.0 (2026-10-31) / 10.0.2 | | Outbox frameworks. A send-once ledger is one table with a unique key. Overkill. |

Business fact: Twilio retired SendGrid's free email plan from 2025-05-27; new accounts get a 60-day trial
([changelog](https://www.twilio.com/en-us/changelog/sendgrid-free-plan), [trial article](https://support.sendgrid.com/hc/en-us/articles/35270136965403)).
A public library whose only provider costs money to test is harder to maintain.

**Verdict: Thin layer.** One interface, a SendGrid web-API provider (no SDK needed), an Azure Communication Services
provider and an SMTP provider on MailKit; the send-once ledger is ours.

---

# Data and content

## 15. Ballast.Excel: Use instead

Sources read 2026-10-31.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [ClosedXML](https://www.nuget.org/packages/ClosedXML) | MIT | 0.105.1 (2026-07-25) | yes; commit 2026-10-21 | Read and write. Already used by SharedScraping. Depends on DocumentFormat.OpenXml, ClosedXML.Parser and SixLabors.Fonts `[1.0.0, 3.0.0)`; Fonts 2.x is under the Six Labors Split Licence, whose "Transitive Package Dependency" clause gives Apache-2.0 (read 2.1.3's licence page). |
| [DocumentFormat.OpenXml](https://www.nuget.org/packages/DocumentFormat.OpenXml) | MIT | 3.5.1 (2026-03-18) | yes | Microsoft's low-level SDK. |
| [MiniExcel](https://www.nuget.org/packages/MiniExcel) | Apache-2.0 | 1.46.0 (2026-08-22) | yes | Streaming. A 2.0 preview exists. |
| [SpreadCheetah](https://www.nuget.org/packages/SpreadCheetah) | MIT | 1.28.0 (2026-07-02) | yes | Write-only streaming. |
| [ExcelDataReader](https://www.nuget.org/packages/ExcelDataReader) | MIT | 3.9.0 (2026-06-16) | yes | Read-only. |
| [Sylvan.Data.Excel](https://www.nuget.org/packages/Sylvan.Data.Excel) | MIT | 0.5.8 (2026-08-10) | yes | Read. |
| [EPPlus](https://www.nuget.org/packages/EPPlus) | Polyform Noncommercial 1.0.0 | 8.7.2 (2026-10-15) | yes | **No** (section A). |
| [NPOI](https://www.nuget.org/packages/NPOI) | fee agreement in the package | 2.8.1 (2026-09-22) | yes | **No** (section A). |

**Verdict: Use instead (ClosedXML).** Ballast.Excel would only wrap it. Rule 1 (report caps) is a few lines at the call site
(file size and row cap before reading). If two apps repeat the same cap code, a thin helper is justified later.

## 16. Ballast.Translations: Thin layer

Sources read 2026-10-31.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| resx + [Microsoft.Extensions.Localization](https://www.nuget.org/packages/Microsoft.Extensions.Localization) | MIT | 10.0.14 (2026-10-25) | yes | Server. MAUI also uses resx ([docs](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/localization)). |
| [LocalizationResourceManager.Maui](https://www.nuget.org/packages/LocalizationResourceManager.Maui) | MIT | 1.2.6 (2026-05-14) | yes | Switch language at run time in MAUI. |
| [OrchardCore.Localization.Core](https://www.nuget.org/packages/OrchardCore.Localization.Core) 3.0.1 (2026-07-09), [Karambolo.PO](https://www.nuget.org/packages/Karambolo.PO) 1.13.0 (2026-03-22) | BSD-3 / licence file not read | | yes | `.po` files. |
| [i18next](https://www.npmjs.com/package/i18next) | MIT | 26.5.12 (2026-10-30) | yes | Web. |
| [@formatjs/intl](https://www.npmjs.com/package/@formatjs/intl) 6.1.4, [@lingui/core](https://www.npmjs.com/package/@lingui/core) 6.11.0, [vue-i18n](https://www.npmjs.com/package/vue-i18n) 11.4.16, [typesafe-i18n](https://www.npmjs.com/package/typesafe-i18n) 5.27.1 | MIT | | yes | Web alternatives. |
| [i18next-parser](https://www.npmjs.com/package/i18next-parser) | MIT | 9.4.0 (2026-02-22) | **deprecated**: "use i18next-cli instead" | |

I did not find a package that unit-tests two language files against each other for missing keys. Not searched exhaustively:
**NEEDS VERIFICATION.**

**Verdict: Thin layer.** Do not build a string-resource system. Ship the parity-test helpers only (same keys, same
placeholders), against resx and JSON files.

---

# Identity and app services

## 17. Ballast.Auth: Thin layer

Sources read 2026-10-31. Rules involved: 1, 4.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [ASP.NET Core Identity](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore) | MIT | 10.0.14 (2026-10-25) | yes | Users, password hashing, lockout, token providers. .NET 10 adds passkeys ([release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0)). |
| `MapIdentityApi` (same package) | MIT | | | Its tokens "aren't standard JSON Web Tokens"; it "isn't intended to be a full-featured identity service provider or token server"; sign-out does not invalidate bearer tokens ([docs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)). No reuse detection. **Does not meet the brief.** |
| [JwtBearer](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer) / [Microsoft.IdentityModel.JsonWebTokens](https://www.nuget.org/packages/Microsoft.IdentityModel.JsonWebTokens) | MIT | 10.0.14 / 8.23.0 (2026-09-18) | yes | Token validation and creation. Already used. |
| [Google.Apis.Auth](https://www.nuget.org/packages/Google.Apis.Auth) | Apache-2.0 | 1.78.0 (2026-10-30) | yes | Google ID-token check. Already used. |
| [Microsoft.AspNetCore.Authentication.OpenIdConnect](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.OpenIdConnect) 10.0.14, [Microsoft.Identity.Web](https://www.nuget.org/packages/Microsoft.Identity.Web) 4.16.1 (2026-10-13), [Microsoft.Graph](https://www.nuget.org/packages/Microsoft.Graph) 6.9.0 (2026-10-22) | MIT (Graph licence file reads MIT) | | yes | Entra staff sign-in and directory sync. |
| [Fido2](https://www.nuget.org/packages/Fido2) | MIT | 4.3.2 (2026-10-29) | yes | WebAuthn, if you want passkeys beyond Identity's. |
| [OpenIddict](https://www.nuget.org/packages/OpenIddict.AspNetCore) | Apache-2.0 | 7.7.2 (2026-10-10) | yes | A real OAuth 2 / OIDC server. Its source: `RefreshTokenReuseLeeway` (default 30 s) for concurrent use of a redeemed refresh token, and when a redeemed token is presented again "revoke the entire chain of tokens associated with the authorization" ([options](https://github.com/openiddict/openiddict-core/blob/dev/src/OpenIddict.Server/OpenIddictServerOptions.cs), [handler](https://github.com/openiddict/openiddict-core/blob/dev/src/OpenIddict.Server/OpenIddictServerHandlers.Exchange.cs)). |
| [Duende.IdentityServer](https://www.nuget.org/packages/Duende.IdentityServer) | commercial | 8.0.10 (2026-10-15) | yes | Section A. Not for a public package. |
| [Passwordless.AspNetCore](https://www.nuget.org/packages/Passwordless.AspNetCore) | Apache-2.0 | 2.0.0 (2024-08-13) | no | Bitwarden's hosted service. Not self-contained. |
| [Microsoft.Identity.Client](https://www.nuget.org/packages/Microsoft.Identity.Client) (MSAL) | MIT | 4.92.0 (2026-10-29) | yes | Client sign-in; `net8.0-android34.0` target. |

**Verdict: Thin layer.** Everything below the flows already exists and is used by the apps. What stays ours: the rotating
refresh token with reuse detection on the apps' own endpoints, and the staff roles rule. Do not depend on Duende.

An option for the owner, not a recommendation to act now: OpenIddict does rotation, reuse leeway and chain revocation, but
you would run an OIDC server with its own endpoints and its own EF stores, which changes the apps' wire contract. It makes sense
only if third-party clients need to sign in. Whether to adopt it is a design decision to take with the apps' auth owner.

## 18. Ballast.AppAttestation: Thin layer

Sources read 2026-10-31.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [Google.Apis.PlayIntegrity.v1](https://www.nuget.org/packages/Google.Apis.PlayIntegrity.v1) | Apache-2.0 | 1.76.0.4270 (2026-09-15) | yes | Server decode of the verdict. Already used by TransitInfoAPI. |
| [Xamarin.Google.Android.Play.Integrity](https://www.nuget.org/packages/Xamarin.Google.Android.Play.Integrity) | Microsoft-published binding; licence file points to the binding repo | 1.6.0.4 (2026-09-02) | yes; `net10.0-android36.0` | Client call. Already used by the app. |
| [Xamarin.Firebase.AppCheck.PlayIntegrity](https://www.nuget.org/packages/Xamarin.Firebase.AppCheck.PlayIntegrity) | binding | 119.4.1 | | Firebase App Check client. [FirebaseAdmin](https://www.nuget.org/packages/FirebaseAdmin) 3.7.0 (Apache-2.0, 2026-09-24): a code search of its repository for "AppCheck" found only `AGENTS.md`, so no server-side verification there (**NEEDS VERIFICATION**). |

Docs for the flow: [standard requests](https://developer.android.com/google/play/integrity/standard),
[verdicts](https://developer.android.com/google/play/integrity/verdicts).

**Verdict: Thin layer.** Google's packages decode the verdict; nothing issues "short-lived app session tokens" with a
log-only rollout switch. That glue is ours.

## 19. Ballast.Feedback: Keep ours

Sources read 2026-10-31.

- [Sentry.Maui](https://www.nuget.org/packages/Sentry.Maui) 6.12.1 (MIT, 2026-10-13): `SentrySdk.CaptureFeedback(...)` exists
  ([docs](https://docs.sentry.io/platforms/dotnet/guides/maui/user-feedback/)). The page shows no MAUI form and points to
  Sentry's hosted ingest. The feedback would leave your system.
- App Center is retired (section 9).

**Verdict: Keep ours.** A table, one endpoint and a form. The rider-feedback AI job reads your own table, and the text is
masked before it goes to an AI. If you accepted a third-party store, Sentry would be a "Use instead", but that is a privacy
decision, not a code one.

## 20. Ballast.AppUpdates: Thin layer

Sources read 2026-10-31.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [Xamarin.Google.Android.Play.App.Update](https://www.nuget.org/packages/Xamarin.Google.Android.Play.App.Update) | Microsoft binding | 2.1.0.20 (2026-09-02) | yes; `net10.0-android36.0` | Play's flexible and immediate flows ([docs](https://developer.android.com/guide/playcore/in-app-updates)). Already used by the app. The page does not say what happens to builds not installed from Play: **NEEDS VERIFICATION.** |
| [Oscore.Maui.Android.InAppUpdates](https://www.nuget.org/packages/Oscore.Maui.Android.InAppUpdates) 1.5.1, [Oscore.Maui.AppStoreInfo](https://www.nuget.org/packages/Oscore.Maui.AppStoreInfo) 1.3.1 | licence not read | | not checked | Small community wrappers. |

**Verdict: Thin layer.** The compare of build numbers and the notice are server-driven and work for any install, including the
owner's sideloaded builds; that is ours. The Play-managed update is a call into the existing binding.

## 21. Ballast.Admin: Keep ours

Sources read 2026-10-31.

- UI kits, all MIT: [@tabler/core](https://www.npmjs.com/package/@tabler/core) 1.7.3 (2026-10-29),
  [admin-lte](https://www.npmjs.com/package/admin-lte) 4.10.0 (2026-10-01), [@coreui/coreui](https://www.npmjs.com/package/@coreui/coreui)
  5.9.1 (2026-10-04), [bootstrap](https://www.npmjs.com/package/bootstrap) 5.3.8 (2025-08-26). They give layout and components.
  A "staff & roles page" depends on your auth model.
- [Microsoft.AspNetCore.Identity.UI](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.UI) 10.0.14 (MIT) is Razor
  pages for account management. I did not check whether it has a staff-and-roles page: **NEEDS VERIFICATION.**

**Verdict: Keep ours.** This is the weakest case for a public package: it is mostly glue to your Auth and your admin pages.
Keep it in the apps until a second project needs it. If you build the layout, base it on Tabler or Bootstrap CSS instead of
hand-made CSS.

## 22. Ballast.Maui: Thin layer

Sources read 2026-10-31. Trimmed Android matters here.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [CommunityToolkit.Maui](https://www.nuget.org/packages/CommunityToolkit.Maui) | MIT | 15.0.1 (2026-08-21) | yes; `net10.0-android36.0`; `IsAotCompatible` in its csproj | Helpers, behaviors, converters. Already used. |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm) | MIT | 8.4.2 (2026-03-25) | yes (repo commit 2026-03-24) | View-model base classes, source-generated commands. Already used. Do not write another. |
| [Plugin.AdMob](https://www.nuget.org/packages/Plugin.AdMob) | MIT | 10.0.120.1 (2026-10-31) | yes; `net10.0-android36.0` | Every ad format; the README says GDPR consent through Google's User Messaging Platform is built in. Already used. [Repo](https://github.com/marius-bughiu/Plugin.AdMob) |
| [Plugin.MauiMTAdmob](https://www.nuget.org/packages/Plugin.MauiMTAdmob) | custom "License Agreement" file (not analysed) | 3.0.0 (2026-10-16) | yes | Read the licence before use. |
| [Soenneker.Maui.Admob](https://www.nuget.org/packages/Soenneker.Maui.Admob) | not read | 11.0.604 | | |

**Verdict: Thin layer.** Do not wrap AdMob or write view-model bases. Publish only helpers the toolkits lack, each with a
trim test.

---

# Browser JavaScript

## 23. @ballast/web: Thin layer

Sources read 2026-10-31 (npm registry).

| Need | Candidates | Licence / latest / active |
|---|---|---|
| Escaping | [escape-html](https://www.npmjs.com/package/escape-html) 1.0.3 (2015, no change needed); [he](https://www.npmjs.com/package/he) 2.0.0 (2026-10-04); [lit-html](https://www.npmjs.com/package/lit-html) 3.3.3 (BSD-3, 2026-05-14): templates that escape for you. [DOMPurify](https://www.npmjs.com/package/dompurify) 3.4.17 (MPL-2.0 OR Apache-2.0, 2026-10-12) cleans HTML; it does not escape text. | MIT unless noted |
| Fetch | [ky](https://www.npmjs.com/package/ky) 2.2.0 (2026-10-09), [ofetch](https://www.npmjs.com/package/ofetch) 1.5.1 (2025-11-01), [axios](https://www.npmjs.com/package/axios) 1.21.0 (2026-10-27), [wretch](https://www.npmjs.com/package/wretch) 3.0.9, [up-fetch](https://www.npmjs.com/package/up-fetch) 2.6.1 | MIT, active |
| Reading problem JSON | [problem-json](https://www.npmjs.com/package/problem-json) 0.3.0 (2017); [http-problem-details](https://www.npmjs.com/package/http-problem-details) 0.1.7 (2025-01-24) | MIT, effectively stale |
| Sessions | [oidc-client-ts](https://www.npmjs.com/package/oidc-client-ts) 3.5.0 (Apache-2.0, 2026-03-13); [@azure/msal-browser](https://www.npmjs.com/package/@azure/msal-browser) 5.27.0 (MIT, 2026-10-27); [js-cookie](https://www.npmjs.com/package/js-cookie) 3.0.8 | Protocol libraries. The apps' session is an app cookie plus a 401 re-attest handoff. |

Admin pages escape with a function (`Shell.esc`). The CLAUDE.md note says string-concatenated HTML is not scanned by the
guard test. A tagged template that escapes by default (lit-html, or a 20-line tag of your own) removes that class of bug.

**Verdict: Thin layer.** About 50 lines: fetch that reads `{type,title,status,code,traceId}` into an error object. Escaping:
prefer an escaping tag over a hand-called function. No session library beyond what the app handoff needs.

## 24. @ballast/admin: Keep ours

Same evidence as section 21. A shell (nav, auth guard, tables) tied to your pages. Base the styling on Tabler or Bootstrap.
Weak case for a public package until a second project needs it.

---

# Testing, build and delivery

## 25. Ballast.Testing: Thin layer

Sources read 2026-10-31.

| Need | Candidate | Licence | Latest (date) | Active |
|---|---|---|---|---|
| Runner | [xunit.v3](https://www.nuget.org/packages/xunit.v3) | Apache-2.0 | 4.0.2 (2026-10-09) | yes |
| Per-run SQL database | [Testcontainers.MsSql](https://www.nuget.org/packages/Testcontainers.MsSql) | MIT | 4.16.0 (2026-10-08) | yes |
| Reset data | [Respawn](https://www.nuget.org/packages/Respawn) | Apache-2.0 | 7.0.0 (2025-11-30) | yes (repo commit 2026-01-19) |
| API tests | [Microsoft.AspNetCore.Mvc.Testing](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing) | MIT | 10.0.14 | yes |
| Fake clock | [FakeTimeProvider](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing) | MIT | 10.11.0 | yes |
| Snapshots | [Verify.XunitV3](https://www.nuget.org/packages/Verify.XunitV3) | MIT | 33.3.4 (2026-10-16) | yes |
| Architecture rules | [TngTech.ArchUnitNET](https://www.nuget.org/packages/TngTech.ArchUnitNET) 0.13.5 (2026-10-26) | Apache-2.0 | | yes |
| | [NetArchTest.Rules](https://www.nuget.org/packages/NetArchTest.Rules) 1.3.2 (2021-05-23) | MIT | | repo's last commit 2023-07-03: stale |
| Assertions | [Shouldly](https://www.nuget.org/packages/Shouldly) 4.3.0 (2025-01-23), [AwesomeAssertions](https://www.nuget.org/packages/AwesomeAssertions) 9.6.0 (2026-08-20) | BSD-3 / Apache-2.0 | | AwesomeAssertions yes. Its README says the licence "will never change". |
| | [FluentAssertions](https://www.nuget.org/packages/FluentAssertions) 8.11.0 | Xceed non-commercial | | **Avoid** (section A) |

Source-scan guards (the tests that read source files and fail on `DateTime.Now`, unlisted hosted services and so on): I found
no package for these. They are repo-specific.

**Verdict: Thin layer.** Ship the glue: a per-run database name, a source-scan guard base class and the architecture-rule
helpers. Do not reference FluentAssertions 8, so consumers of the test package are not pulled into Xceed's terms.

## 26. Ballast.Build: Thin layer

Sources read 2026-10-31.

| Need | Existing | Licence / latest |
|---|---|---|
| Version from tags | [MinVer](https://www.nuget.org/packages/MinVer) 8.0.0 (2026-09-05); [Nerdbank.GitVersioning](https://www.nuget.org/packages/Nerdbank.GitVersioning) 3.10.94; [GitVersion.MsBuild](https://www.nuget.org/packages/GitVersion.MsBuild) 6.8.2 | Apache-2.0 / MIT / MIT, active |
| Source Link | [Microsoft.SourceLink.GitHub](https://www.nuget.org/packages/Microsoft.SourceLink.GitHub) 10.0.403 (2026-10-25) | MIT |
| Analyzers | .NET SDK analyzers ([Microsoft.CodeAnalysis.NetAnalyzers](https://www.nuget.org/packages/Microsoft.CodeAnalysis.NetAnalyzers) 10.0.403), [Meziantou.Analyzer](https://www.nuget.org/packages/Meziantou.Analyzer) 3.0.318 (2026-10-30), [Roslynator.Analyzers](https://www.nuget.org/packages/Roslynator.Analyzers) 5.0.1 (2026-10-04), [PublicApiAnalyzers](https://www.nuget.org/packages/Microsoft.CodeAnalysis.PublicApiAnalyzers) 5.6.0 | MIT / Apache-2.0, active |
| Packaged coding standard | [Meziantou.DotNet.CodingStandard](https://www.nuget.org/packages/Meziantou.DotNet.CodingStandard) 1.0.181 (2025-08-27) | licence file not read; no release in 12 months |
| Package vulnerabilities | NuGetAudit, built in ([docs](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)) | |
| Pinned versions | Central package management, built in | |
| StyleCop | [StyleCop.Analyzers](https://www.nuget.org/packages/StyleCop.Analyzers) | stable 1.1.118 is from 2019; a beta from 2026-10-21 |

**Verdict: Thin layer.** A props-and-targets package holding the house settings (nullable, analysis level, warnings as errors,
trim and AOT flags) and referencing the analyzers above. It should contain no tool of its own.

## 27. Reusable CI workflows: Thin layer

Sources read 2026-10-31.

- GitHub reusable workflows are a platform feature ([docs](https://docs.github.com/en/actions/sharing-automations/reusing-workflows)).
- Publish with `NuGet/login@v1` and trusted publishing (section C). The short-lived key lasts one hour, so request it right before the push.
- Official steps (checkout, setup-dotnet, cache) are not Ballast's to rewrite.

**Verdict: Thin layer.** The workflows compose official actions with the house order: restore, build with warnings as errors,
format check, tests, pack, publish by trusted policy. They are YAML in the repo.

## 28. Semgrep rules: Keep ours

Sources read 2026-10-31.

- Engine: Semgrep is LGPL-2.1 ([repo](https://github.com/semgrep/semgrep), v1.183.0, 2026-10-29). The fork
  [Opengrep](https://github.com/opengrep/opengrep) is LGPL-2.1 (v1.33.1, 2026-10-29).
- Semgrep's own rules: Semgrep Rules Licence v1.0, internal business use only
  ([licensing](https://docs.semgrep.dev/licensing)). Do not copy them into a public repository.
- .NET alternatives: [DevSkim](https://github.com/microsoft/DevSkim) (MIT, v1.0.100, 2026-09-29),
  [SecurityCodeScan](https://github.com/security-code-scan/security-code-scan) (LGPL-3.0, last release 2022-09-05, stale).

**Verdict: Keep ours.** Write rules for your own patterns (`DateTime.Now`, `Parse` instead of `TryParse`, a catch that
swallows). Ship them under MPL-2.0. The engine stays a separate tool.

## 29. Deploy module: Thin layer

Sources read 2026-10-31.

- [Azure/webapps-deploy](https://github.com/Azure/webapps-deploy): MIT, v2.2.19 (2026-03-27), commit 2026-09-15.
- `az webapp deploy` and zip deploy ([docs](https://learn.microsoft.com/en-us/azure/app-service/deploy-zip)).
- [Azure Developer CLI](https://github.com/Azure/azure-dev): MIT, v1.37.2 (2026-10-29).
- App Service [health check](https://learn.microsoft.com/en-us/azure/app-service/monitor-instances-health-check).

I read these repositories' descriptions, not their full docs. I did not find a step that waits until `/health` reports the
deployed commit, tags the release and rolls back by tag. **NEEDS VERIFICATION** by reading the action's inputs.

**Verdict: Thin layer.** Call `az` for the push. The proof ("health reports the commit and ready is 200"), the tag and the
rollback by tag are ours.

---

# Mooring (private)

## 30. Mooring.Documents: Thin layer

Sources read 2026-10-31. Licences matter here too: a closed app still pays for a commercial licence.

| Job | Candidate | Licence | Latest (date) | Active |
|---|---|---|---|---|
| Barcodes | [ZXing.Net](https://www.nuget.org/packages/ZXing.Net) | Apache-2.0 | 0.16.11 (2025-10-25) | repo commit 2026-10-28; no release since |
| | [ZXing.Net.Maui](https://www.nuget.org/packages/ZXing.Net.Maui) 0.10.4 (2026-08-14), [BarcodeScanning.Native.Maui](https://www.nuget.org/packages/BarcodeScanning.Native.Maui) 3.1.0 (2026-07-28) | MIT | | yes |
| PDF text | [PdfPig](https://www.nuget.org/packages/PdfPig) | Apache-2.0 | 0.1.16 (2026-08-22) | yes |
| PDF to image | [PDFtoImage](https://www.nuget.org/packages/PDFtoImage) | MIT | 5.4.0 (2026-08-16) | yes |
| | [Docnet.Core](https://www.nuget.org/packages/Docnet.Core) | MIT | 2.6.0 (2023-09-04) | **no** (section B) |
| | [Pdfium.Net.SDK](https://www.nuget.org/packages/Pdfium.Net.SDK) | commercial licence agreement | 5.1.4 (2026-10-05) | |
| | [PDFsharp](https://www.nuget.org/packages/PDFsharp), [iText](https://www.nuget.org/packages/itext) | AGPL-3.0 | | **No** |
| Mail | [MimeKit](https://www.nuget.org/packages/MimeKit) / [MimeKitLite](https://www.nuget.org/packages/MimeKitLite) | MIT | 4.18.2 (2026-10-21) / 4.18.1 | yes |
| iCal | [Ical.Net](https://www.nuget.org/packages/Ical.Net) | MIT | 5.2.4 (2026-10-31); 6.0.0-pre.2 | yes |
| Dates in many languages | [Microsoft.Recognizers.Text.DateTime](https://www.nuget.org/packages/Microsoft.Recognizers.Text.DateTime) | MIT | 1.8.13 (2025-02-19) | marked deprecated "Legacy" on nuget.org. README: full support for Chinese, English, French, Spanish, Portuguese, German, Italian, Turkish, Hindi, Dutch; partial for Japanese, Korean, Arabic, Swedish ([README](https://github.com/microsoft/Recognizers-Text)). **No Croatian, Serbian, Slovene or Hungarian.** |
| OCR (only if scanned tickets) | [Tesseract](https://www.nuget.org/packages/Tesseract) 5.2.0 (2022-11-30, stale); [Sdcb.PaddleOCR](https://www.nuget.org/packages/Sdcb.PaddleOCR) 3.3.1 (2026-05-26) | Apache-2.0 | | |

**Verdict: Thin layer.** These packages are already the app's choices. The date reader for your 15 languages is **Keep
ours**: the only published recogniser is deprecated and lacks your main languages. Build it on `CultureInfo` month and day
names plus explicit patterns. Replace Docnet.Core. Never use ImageSharp, PDFsharp or iText.

## 31. Mooring.Scraping: Keep ours

Sources read 2026-10-31.

| Part | Candidate | Licence | Latest (date) | Active |
|---|---|---|---|---|
| HTML | [AngleSharp](https://www.nuget.org/packages/AngleSharp) (used) | MIT | 1.9.0 (2026-10-30) | yes |
| | [AngleSharp.XPath](https://www.nuget.org/packages/AngleSharp.XPath) 2.0.10; [HtmlAgilityPack](https://www.nuget.org/packages/HtmlAgilityPack) 1.13.1 | MIT | | yes |
| | [Fizzler](https://www.nuget.org/packages/Fizzler) 1.3.1 | LGPL-3.0-or-later | 2024-07-10 | no |
| JSON path | [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) 13.0.4 (2025-09-16): `SelectTokens` ([docs](https://www.newtonsoft.com/json/help/html/SelectToken.htm)) | MIT | | 2 prerelease builds in 12 months |
| | [JsonPath.Net](https://www.nuget.org/packages/JsonPath.Net) 3.0.3 (2026-10-24) | **fee agreement** (section A) | | yes |
| | [Jsonata.Net.Native](https://www.nuget.org/packages/Jsonata.Net.Native) 3.0.0 (2026-02-26) | MIT (file read) | | yes |
| | [JsonCons.JsonPath](https://www.nuget.org/packages/JsonCons.JsonPath) 1.1.0 (2021-08-25) | licence file not read | | no |
| PDF, Excel, CSV | PdfPig, ClosedXML, [CsvHelper](https://www.nuget.org/packages/CsvHelper) 34.0.0 (MS-PL OR Apache-2.0, 2026-10-28) | | | yes |
| Rendered pages | [Microsoft.Playwright](https://www.nuget.org/packages/Microsoft.Playwright) 1.64.1 (MIT, 2026-10-22); [PuppeteerSharp](https://www.nuget.org/packages/PuppeteerSharp) 25.15.0 (MIT, 2026-10-21) | | | yes |
| Crawlers | [Abot](https://www.nuget.org/packages/Abot) 2.0.70 (Apache-2.0, 2021-08-09) | | | no |

None of these is a request-and-mapping engine configured from a database with the SSRF rules, held runs and AI repair
described in the GetThere docs. That engine is the product.

**Verdict: Keep ours.** Keep building on AngleSharp, PdfPig and ClosedXML. For JSON path, decide on JsonPath.Net's binary
terms or move to Newtonsoft.Json. Ballast.Http's SSRF handler should sit under the engine's fetcher.

## 32. Mooring.Ai: Keep ours

Sources read 2026-10-31.

| Candidate | Licence | Latest (date) | Active | Fit |
|---|---|---|---|---|
| [Anthropic](https://www.nuget.org/packages/Anthropic) (official, [repo](https://github.com/anthropics/anthropic-sdk-csharp)) | Apache-2.0 | 13.3.0 (2026-10-23) | yes, 77 stable releases in 12 months | Already used by SharedAi. The repo has a `Microsoft.Extensions.AI` adapter (`AnthropicClientExtensions.cs`, a `ChatClientExample`). |
| [Anthropic.SDK](https://www.nuget.org/packages/Anthropic.SDK) (unofficial) | MIT | 5.10.0 (2026-02-20) | 2 releases | Not needed. |
| [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI) | MIT | 10.11.0 (2026-10-14) | yes | A provider-neutral `IChatClient`. |
| [Microsoft.Agents.AI](https://www.nuget.org/packages/Microsoft.Agents.AI) | MIT | 1.27.0 (2026-10-30) | yes | Agent framework. |
| [Microsoft.SemanticKernel](https://www.nuget.org/packages/Microsoft.SemanticKernel) | MIT | 1.81.0 (2026-10-06) | yes | Orchestration. |
| [LangChain](https://www.nuget.org/packages/LangChain) | MIT | 0.17.1 (2026-04-30) | 1 stable | Community port. |

The three-role engine, the switch-on bar, the fall-back, the daily report, trials, the watch-out list and the budget gate
are policy about when AI may act and how it is undone. No framework has that.

**Verdict: Keep ours**, on the official SDK. House rule 3 matters if you add a second model through `IChatClient`: a fall-back
to another model must be visible, never silent.

## 33. Mooring.Audit: Thin layer

Sources read 2026-10-31.

| Job | Existing tool | Licence / latest |
|---|---|---|
| Secrets | [gitleaks](https://github.com/gitleaks/gitleaks) v8.30.1 (2026-03-21) | MIT |
| Known-vulnerable packages | NuGetAudit (built in); [OSV-Scanner](https://github.com/google/osv-scanner) v2.7.0 (2026-10-12) | Apache-2.0 |
| Images, IaC, SBOM | [Trivy](https://github.com/aquasecurity/trivy) v0.76.1 (2026-10-21) | Apache-2.0 |
| Pattern scan | Semgrep or Opengrep with your rules (section 28); [DevSkim](https://github.com/microsoft/DevSkim) | LGPL-2.1 / MIT |
| Code scanning | CodeQL | Free for public repos; private needs a paid GitHub licence (section A) |
| Quality platform | [SonarQube](https://github.com/SonarSource/sonarqube) Community | LGPL-3.0, release 26.10.0 (2026-10-12) |
| AI review | [anthropics/claude-code-security-review](https://github.com/anthropics/claude-code-security-review) (MIT; last commit 2026-02-11), [anthropics/claude-code-action](https://github.com/anthropics/claude-code-action) (MIT; commit 2026-10-31) | |
| Avoid | TruffleHog (AGPL-3.0), SecurityCodeScan (stale) | |

**Verdict: Thin layer.** The mechanical scans of the "no-token scan" can be run by these tools and read into the ledger. The
workflow where agents find, skeptics refute and Claude rules is yours.

---

# Do first

1. **Do not switch the six basics.** Publish them as they are.
2. **Drop Ballast.Telemetry and Ballast.Excel** from the plan. Write a short guide page for each: OpenTelemetry plus one Azure
   distro, and ClosedXML.
3. **Build Ballast.Jobs, Ballast.Http and Ballast.SqlServer on the packages named above**, not on hand-written copies.
4. **Add a pre-release licence check:** read the licence file inside every dependency's `.nupkg`. This is how NPOI and
   JsonPath.Net would have been missed.
5. **Decide on JsonPath.Net** for Mooring.Scraping, and replace Docnet.Core in GetThereAPI.
6. **Try the `Ballast.` prefix reservation and the npm scope now**, and set up trusted publishing before the first push.
7. **Treat Ballast.Admin, @ballast/admin, Ballast.Feedback as in-app code** until a second project asks for them.

# What I did not check

- I did not read the source of most packages, only metadata, READMEs and docs.
- I did not run any package. The only code I ran was a few built-in .NET calls (tested lines in sections 2 and 4).
- Trimmed-MAUI behaviour is confirmed only where the section says so (NodaTime, NodaMoney and CommunityToolkit.Maui have
  `IsAotCompatible` set in their project files; libphonenumber-csharp's README says it is trim- and AOT-annotated; the Plugin.AdMob,
  Xamarin Play and Sentry.Maui packages list `net10.0-android36.0`). For every other package it is **NEEDS VERIFICATION**.
- Download counts are left out on purpose.
