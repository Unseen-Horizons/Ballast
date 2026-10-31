# Ballast

Ballast is a set of general building blocks for .NET apps, by Unseen Horizons. Each library is its own NuGet package, and
the basic libraries depend on no other package.

## Libraries

- [Ballast.Errors](src/Ballast.Errors): an error type with stable codes, the problem answer shape, and a reader for problem answers.
- [Ballast.Paging](src/Ballast.Paging): a page request with safe limits, an overflow-safe offset, and the paged result shape.
- [Ballast.Time](src/Ballast.Time): time zone lookup, place time, and a local clock that stays in one zone.
- [Ballast.Text](src/Ballast.Text): accent-free text folding and a personal-data masker.
- [Ballast.Geo](src/Ballast.Geo): coordinate checks, distance, bounding boxes and polygon containment.
- [Ballast.Money](src/Ballast.Money): currency codes, rounding to minor units, line totals and money formatting.

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
