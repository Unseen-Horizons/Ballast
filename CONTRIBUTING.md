# Contributing to Ballast

Ballast is licensed under the Mozilla Public License 2.0 (MPL-2.0). Every contribution comes under the same licence.

## Build and test

You need the .NET SDK. `global.json` pins 10.0.401. If that SDK is not installed, a newer 10.0 SDK is used (`rollForward: latestFeature`).

```bash
dotnet build Ballast.slnx -warnaserror
dotnet format Ballast.slnx --verify-no-changes
dotnet test --solution Ballast.slnx
dotnet pack Ballast.slnx -c Release -o artifacts/packages
```

CI runs the same four steps on every push and pull request. A change must pass them.

## What a change contains

- **The file header.** Every `.cs` file starts with the MPL-2.0 notice. Copy it from an existing file. Without it the
  build fails (IDE0073 is a warning, and CI builds with `-warnaserror`).
- **Documentation for every public member.** CS1591 is a warning, so an undocumented public member fails the build.
- **The usage guide.** Every option and every way a library fails is written down in `docs/<Name>.md`.
- **The package README.** A change to a library's behaviour updates its README, including its runnable example.
- **A changelog entry.** `CHANGELOG.md` gets a line for the change.
- **Tests.** A behaviour change has a test that fails before the change and passes after it.

## Rules

Read [CLAUDE.md](CLAUDE.md) for the full rules. The ones that most often decide a review:

- A library names no app and no product. Values an app chooses come in through options or an interface.
- A library depends only on .NET and the packages its own area needs. The basic libraries depend on no package.
- A failure is reported as a failure. Nothing is silently replaced with a guess or a default.
- Every instant is UTC, and every local time names its time zone.
- An error code is a public contract. A released code is never renamed, reused or changed.

## Releases

Contributors do not publish. A release is a version tag that the owner approves in the `release` environment before the
packages are published to GitHub Packages and the GitHub Release. The steps are in [docs/releasing.md](docs/releasing.md), and the one-time setup is in
[docs/setup/publishing.md](docs/setup/publishing.md).
