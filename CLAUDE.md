# Ballast: rules for agents

Ballast is a set of general building blocks, published as NuGet packages under MPL-2.0. These rules apply to every change.

## Layout

- `src/Ballast.<Name>/`: one library. Its project, its code and its package `README.md`.
- `tests/Ballast.<Name>.Tests/`: its xUnit v3 tests. Test projects are not packed.
- `docs/<Name>.md`: its usage guide. `docs/README.md` lists them.
- `Directory.Build.props`, `src/Directory.Build.props` and `tests/Directory.Build.props`: the shared settings.
  `Directory.Packages.props` holds every package version. `global.json` pins the SDK and the test runner.
- `CHANGELOG.md`: one section per shared version.
- `Ballast.slnx`: every project.

Build, test and pack commands are in `CONTRIBUTING.md`.

## Rules

1. **Standalone.** No app or company product name appears in library code, comments, tests or documentation. The name
   "Zagreb" appears only in tests and examples. A library depends on no app and on no other private library. An app's
   values come in through options or an interface: a zone, a language, a currency list, a message text, a cap.
2. **No sub-libraries.** One library per area. Its optional parts are switched on in its options, not split into more
   packages. Providers live inside their library: `Ballast.Email` includes SendGrid, and `Ballast.Auth` includes Google, Microsoft and the email link.
3. **Dependencies.** A library depends only on .NET and on the packages its own area needs. It never depends on an app or on
   a private library. The six basics depend on no package. Every version lives in `Directory.Packages.props`.
4. **No silent substitution.** A failure is reported as a failure. A cap or a truncation is reported, never applied
   quietly. A value that cannot be parsed or resolved gives `null` or an error, never a guess: an unknown time zone id is
   `null`, not UTC.
5. **Time.** Every instant is UTC, and its name or type says so. Every local time names its zone, and the zone comes from
   the thing it describes, never from the machine's own zone. Tests pin `Europe/Zagreb` within an hour of local midnight
   and on both daylight-saving days.
6. **Stable error codes.** An error code is a public contract. Once released, a code is never renamed, reused or changed.
   A new condition gets a new code.
7. **Documentation in the same change.** A change to a library updates, in the same change: its package README (with a
   runnable example), its `docs/<Name>.md` (every option and every way it fails), and `CHANGELOG.md`. Every public member
   has XML documentation.
8. **File header.** Every `.cs` file starts with the MPL-2.0 notice that `.editorconfig` defines. IDE0073 fails the build
   without it. Do not change the copyright line.
9. **Code conventions.**
   - File-scoped namespaces: `namespace Ballast.Errors;`
   - Collections are `[]`. Never `new List<T>()`.
   - `is null` and `is not null`. `==` and `!=` only inside lambdas.
   - `TryParse`, never `Parse`.
   - Usings are sorted, `System.*` first. `dotnet format` enforces it.
   - Private fields are `_camelCase`. Interfaces start with `I`.
   - Allman braces.
   - Async methods take `CancellationToken ct = default` as the last parameter and pass it to every awaited call.
   - Defaults for strings are `string.Empty`, never `""` and never `null!`.
10. **Files.** Do not change `LICENSE`. Do not add generated output (`bin/`, `obj/`, `artifacts/`) to the repository.
11. **Releases.** Only `.github/workflows/release.yml` publishes, and only after the owner approves the run in the
    `release` environment. Claude never pushes a package or a release tag. See `docs/releasing.md`.
