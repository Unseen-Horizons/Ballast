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

## Install

```powershell
dotnet add package Ballast.Auth --prerelease
```

Each library is versioned with the others (`0.1.0-preview.1`). The package is not published yet; until it is, reference
the project.
