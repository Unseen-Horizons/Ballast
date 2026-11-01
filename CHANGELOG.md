# Changelog

Every library in Ballast carries the same version. Each shared version has one section here, newest first.

## 0.1.0-preview.1

Not yet published. The first preview: six libraries, all new.

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
