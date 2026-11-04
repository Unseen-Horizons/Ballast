# Ballast.Auth

Sign-in for a web app on ASP.NET Core Identity over Entity Framework: rotating refresh tokens with reuse detection,
short access tokens with an instant cut-off, lockout, the account-enumeration guard, single-use e-mail sign-in links,
Google sign-in, and Microsoft staff sign-in. The usage guide, with every option, default, entry point and failure, is
[docs/Auth.md](../../docs/Auth.md).

## Example: a token for an account outside Identity

A portal contact is not an Identity user, but the app still wants to give it a short-lived access token that the same
access pipeline accepts. `AccountAccessTokens` signs the token, and the app's `IAuthAccountStore` says whether the
subject may sign in and what its current security stamp is.

```csharp
using System.Security.Claims;
using Ballast.Auth;

// A contact in a portal is not an Identity user. The store answers whether the subject may sign in and what its security stamp is.
sealed class PortalContacts : IAuthAccountStore
{
    public Task<AuthAccountState?> FindAsync(string subject, CancellationToken ct = default) =>
        Task.FromResult<AuthAccountState?>(subject == "contact:42" ? new AuthAccountState("stamp-1", CanSignIn: true) : null);
}

var options = new AuthOptions
{
    Key = "replace-with-a-secret-of-at-least-32-bytes",
    Issuer = "https://portal.example.test",
    Audience = "portal-clients",
};
var tokens = new AccountAccessTokens(options, new PortalContacts(), TimeProvider.System);
var token = await tokens.CreateAsync("contact:42", TimeSpan.FromMinutes(10));
```

The subject must carry a prefix, such as `contact:42`, so it can never equal an Identity user id. The store splits on
that prefix. The example is compiled and run by `ReadmeExampleTests`.

## Example: the directory sync's last run

`MicrosoftSyncStatus` keeps the last run of the directory sync for an admin page. The job records each run's result, and the page
reads a snapshot. The example is compiled and run by `ReadmeExampleTests`.

```csharp
using Ballast.Auth;

var status = new MicrosoftSyncStatus();
var ended = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

// The job records the run's result when it ends. The library's sync returns the result.
status.Record(new MicrosoftSyncResult(
    MicrosoftSyncOutcome.Applied,
    Failure: null,
    HttpStatus: null,
    Counts: new MicrosoftSyncCounts(UsersAssigned: 12, AppRoles: 3, Updated: 1, SwitchedOff: 0, Created: 0),
    Problems: [new MicrosoftSyncProblem("ROLE_NOT_IN_MICROSOFT", "Retired")],
    RolesNotInMicrosoft: ["Retired"],
    ClientSecretEndsUtc: ended.AddDays(10)), ended);

// The admin page reads a snapshot. The client secret ends in ten days, so it reads Soon.
var snapshot = status.Snapshot(ended);
```

The role and permission claims per request (`AddBallastDynamicClaims`) and the seed password generator are described in
[docs/Auth.md](../../docs/Auth.md).

## Install

```powershell
dotnet add package Ballast.Auth --prerelease
```

Each library is versioned with the others (`0.1.0-preview.1`). The package is not published yet; until it is, reference
the project.
