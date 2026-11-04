# Ballast.Auth: usage guide

Ballast.Auth is the sign-in core of a web app, built on ASP.NET Core Identity over Entity Framework (string or int keys).
It gives refresh tokens that rotate and revoke a whole family on reuse, short access tokens that an account's change
cuts off at once, lockout, an account-enumeration guard, single-use e-mail sign-in links, Google sign-in, Microsoft
staff sign-in, and a signed access token for an account that is not an Identity user.

Every option below has its default. Every refusal has a code, a status and a message, and the codes are the ones clients
translate. The package's short example is in [src/Ballast.Auth/README.md](../src/Ballast.Auth/README.md).

## Entry points

| Entry point | What it does |
|---|---|
| `AddBallastAuth<TUser, TKey, TRole, TContext, TRefresh>(configuration, configure)` | Registers Identity, the JWT bearer pipeline, the sign-in services and the access check. Returns the `AuthenticationBuilder`. |
| `AddBallastEmailLinks<TContext>(services, configure)` | Registers single-use e-mail sign-in links. Needs `Ballast.Email`'s `EmailService` registered. |
| `AddBallastGoogleSignIn<TUser, TKey, TRefresh, TContext>(services, configuration, sectionName = "Google", configure)` | Registers Google sign-in. |
| `AddBallastMicrosoftSignIn<TUser, TKey, TRole, TRefresh, TContext>(services, configuration, section = "MicrosoftSignIn", configure)` | Registers Microsoft staff sign-in and, when it is enabled, its OpenID Connect and cookie schemes. |
| `MapBallastMicrosoftSignIn<TUser, TKey, TRole, TRefresh, TContext>(endpoints, routePrefix = "/auth/microsoft")` | Maps the three Microsoft routes (`start`, `complete`, `exchange`). |
| `AccountAccessTokens` | Signs an access token for a subject from the app's `IAuthAccountStore`. Not an Identity user. |
| `EmailLinkService<TContext>` | Issues, redeems and purges e-mail sign-in links. |
| `GoogleSignInService<TUser, TKey, TRefresh, TContext>` | Signs in with a Google ID token, links and unlinks a Google login. |
| `MicrosoftSignInService<TUser, TKey, TRole, TRefresh, TContext>` | Decides a Microsoft handshake, issues and exchanges one-time codes. |
| `MicrosoftDirectorySync<TUser, TKey, TRole, TRefresh, TContext>` | Runs the directory sync once. The app schedules it. |
| `IAuthAccountStore` | The app's answer for a subject outside Identity: whether it exists, may sign in, and its security stamp. |
| `IAuthAudit` | Where the library's audit rows go. The app writes them in its own table. |
| `IAuthNewAccount<TUser>` | The app's own rows for a new account (for example a wallet). Optional. |
| `IGoogleIdTokenValidator` | Checks a Google ID token. The default calls Google; a test replaces it. |
| `IGoogleTokenVerifier` | The signature check the validator calls: it returns the payload Google signed. The default is `GoogleJsonWebSignatureVerifier` (Google's library). A test replaces it with a fake. |

## AuthOptions

Bound with `ReadJwt` from the section named by `JwtSection`, then checked by `Validate`. Set in code with `configure`.

| Property | Default | Meaning |
|---|---|---|
| `JwtSection` | `Jwt` | The configuration section the JWT settings are read from. |
| `Key` | empty | The HMAC signing key. Required, at least 32 bytes. The placeholder `CHANGE-ME` and a shorter key are refused at start. |
| `Issuer` | empty | The token issuer. Required. |
| `Audience` | empty | The token audience. Required. |
| `AccessTokenMinutes` | 15 | The access token lifetime, in minutes. Between 1 and 1440. |
| `RefreshTokenDays` | 1 | The refresh token lifetime for a session without remember-me, in days. |
| `RefreshTokenDaysRememberMe` | 30 | The refresh token lifetime with remember-me, in days. |
| `ClockSkew` | 30 seconds | The clock difference tolerated when a token's lifetime is checked. |
| `SecurityStampRequired` | `false` | When `true`, a token without a security stamp claim is refused. |
| `LegacyBcryptPasswords` | `false` | When `true`, a password hashed by the old BCrypt format is accepted and rehashed at the next sign-in. |
| `PasswordSignIn` | `All` | Who may sign in with a password: `Off` (nobody, refused before any lookup), `OwnerOnly` (only accounts in `PrivilegedRoleName`), or `All` (every active account that has a password). Microsoft and Google sign-in are not affected. |
| `PasswordSignInBy` | `Email` | The identifier a password sign-in takes: `Email` (the address) or `UserName`. The lookup, the lockout and the hashing time are the same in either mode, and an unknown identifier gets the same answer as a wrong password. Any other value is refused at start. |
| `RequireUniqueEmail` | `true` | Every account must have an address, and no two may share one (Identity's rule). `false` lets an account have no address, and still refuses an address another account has (`AddressUniquenessValidator`). It is refused at start unless `PasswordSignInBy` is `UserName`, because with e-mail sign-in the address is the key. A Microsoft account always has an address, so it is unaffected. |
| `PrivilegedRoleName` | null | The role that `OwnerOnly` admits. |
| `NewAccountRole` | null | The role a new account gets from a sign-up. Null gives none. |
| `Claims` | `AuthClaimNames` | The claim names the tokens use (below). |
| `Failures` | `AuthFailures` | The refusals, their codes, statuses and messages (below). An app that shipped other codes sets them here. |
| `Password` | `AuthPasswordRules` | The password rules (below). |
| `Lockout` | `AuthLockoutRules` | The lockout rules (below). |

Tokens are HS256 JWTs. The access token carries `sub`, the email (when the account has one), the given name, the roles, the security stamp, `jti`, `nbf` and `exp`. The refresh token is 64 random bytes, base64, stored only as its SHA-256.

### AuthClaimNames

| Property | Default | Meaning |
|---|---|---|
| `Subject` | `sub` | The subject claim. The portal client in one app uses `client_id` here, so its tokens keep their wire. |
| `SecurityStamp` | `security_stamp` | The security stamp claim, compared at every request. |

The other names are listed in `AuthClaimNames.cs`.

### Passwords (AuthPasswordRules)

| Property | Default | Meaning |
|---|---|---|
| `RequiredLength` | 12 | The shortest password. |
| `RequireDigit` | `true` | A digit is needed. |
| `RequireUppercase` | `true` | An upper-case letter is needed. |
| `RequireLowercase` | `true` | A lower-case letter is needed. The default matches Identity's own. |
| `RequireNonAlphanumeric` | `true` | A symbol is needed. |
| `RequiredUniqueChars` | 1 | The number of distinct characters needed. |

### Lockout (AuthLockoutRules)

| Property | Default | Meaning |
|---|---|---|
| `MaxFailedAccessAttempts` | 5 | Failed sign-ins before the account is locked. |
| `LockoutDuration` | 15 minutes | How long the lock lasts. |
| `AllowedForNewUsers` | `true` | Whether a new account can be locked out. |

A locked account is answered with `INVALID_CREDENTIALS`, the same answer as an unknown address, so a lock cannot be
used to find accounts.

## Failures (AuthFailures)

Each refusal is an `AuthFailure` with a code, a status and a message. The library throws it as an `AppException`.

| Code | Status | When |
|---|---|---|
| `INVALID_CREDENTIALS` | 401 | Any sign-in that cannot be proven: unknown address, wrong password, locked or password-less account, a Microsoft account, or a user outside `PasswordSignIn`. One answer for all of them. |
| `PASSWORD_SIGNIN_OFF` | 403 | A password sign-in while `PasswordSignIn` is `Off`. |
| `REFRESH_TOKEN_EXPIRED` | 401 | A refresh token that is unknown, expired, revoked, reused, or whose account is locked. One answer for all of them. |
| `INVALID_REFRESH_TOKEN` | 401 | A refresh token that is blank. |
| `USER_NOT_FOUND` | 404 | A signed-in request or admin action names an account that does not exist. |
| `MICROSOFT_ACCOUNT` | 409 | An action a Microsoft account cannot take, such as a password change or a Google link. |
| `NO_PASSWORD_SET` | 409 | A password change on an account with no password. |
| `NO_PASSWORD_ACCOUNT` | 422 | An admin password reset on an account with no password. Set its first password instead. |
| `PASSWORD_ALREADY_SET` | 409 | A first password set on an account that has one. |
| `EMAIL_LINK_INVALID` | 401 | A link that is unknown, used, expired, or for another purpose. |
| `RATE_LIMITED` | 429 | More links were requested in the window than `MaxIssuedPerWindow` allows. |
| `GOOGLE_TOKEN_INVALID` | 401 | A Google token that Google does not vouch for. |
| `GOOGLE_SIGNIN_NOT_CONFIGURED` | 503 | Google sign-in is asked for and no `WebClientId` is set. |
| `GOOGLE_UNREACHABLE` | 503 | Google could not be reached to check the token. The person may retry. |
| `GOOGLE_EMAIL_UNVERIFIED` | 403 | Google has not verified the address. |
| `ACCOUNT_EXISTS_USE_PASSWORD` | 409 | A first Google sign-in whose address belongs to a password account. It is never linked automatically. |
| `GOOGLE_ALREADY_LINKED` | 409 | A link when the account already has a Google login, or the Google account is linked to another user. |
| `LAST_SIGN_IN_METHOD` | 409 | An unlink that would leave the account with no way to sign in (no password). |
| `GOOGLE_NOT_LINKED` | 404 | An unlink when no Google login is on the account. |
| `SIGNIN_CODE_INVALID` | 400 | A Microsoft one-time code that is unknown, used, expired, or whose account is no longer a signed-in Microsoft account. |
| `UNCLASSIFIED` | 400 | An Identity refusal with no code of its own. Its message is Identity's text. |

The Microsoft handshake refusals are not `AuthFailure`s. They are the `error` query of a redirect to the sign-in page,
listed under "Microsoft staff sign-in" below.

## Admin password reset

`AuthAccountService.ResetPasswordAsync(userId, newPassword, ct)` sets a new password for an account, for an administrator,
without the account's current one. The library does not check who calls it: the app decides who may, and checks its own
permission first.

- The new password meets the same rules as any other, through Identity's own validators (`AuthPasswordRules`). A password
  that breaks one is refused with `UNCLASSIFIED`, with Identity's words, and the old password still works.
- The hash is replaced, the security stamp changes, and every refresh token of the account is revoked. An access token
  issued before the reset carries the old stamp, so the access check (`AuthAccessCheck`) refuses it.
- It is audited as `PasswordReset` on the account. The audit row has no field for the administrator who acted, so an app
  that needs that records it in its own audit.
- It refuses with `USER_NOT_FOUND` (404), `MICROSOFT_ACCOUNT` (409) and `NO_PASSWORD_ACCOUNT` (422), listed above.
- It does not lift a lockout. An account locked by failed sign-ins stays locked until the lockout ends.
- The hash, the stamp and the revoke are saved one after another, not in one transaction, as `ChangePasswordAsync` saves
  its steps. The password is replaced first. If a later step fails, the new password stays, and the old sessions may still
  be live until a second reset ends them.

## Accounts outside Identity

A portal contact, a client or a service account is not an Identity user, but the app can still give it an access token
that the same access pipeline accepts. Two parts make this work.

**The subject key.** The subject of such an account must carry a prefix, such as `contact:42`, and must never equal an
Identity user id. An app with both kinds of account uses the prefix, and its `IAuthAccountStore` splits on it to find the
right table. Without the prefix, a portal contact and an Identity user with the same id would share one token.

**`IAuthAccountStore`.** One method:

```csharp
Task<AuthAccountState?> FindAsync(string subject, CancellationToken ct = default);
```

It returns `null` for an unknown subject, and otherwise `AuthAccountState(SecurityStamp, CanSignIn)`. The access check
compares the token's stamp with the store's, at every request. A changed stamp, or `CanSignIn = false`, refuses the
token at once. `IdentityAuthAccountStore<TUser, TKey>` is the default for Identity users; it is registered only if the
app has not registered its own store first.

**`AccountAccessTokens`.** Issues the token:

```csharp
Task<string> CreateAsync(string subject, TimeSpan lifetime, IEnumerable<Claim>? extraClaims = null, CancellationToken ct = default);
```

- `subject` is checked with the store. An unknown subject, or one with `CanSignIn = false`, is refused with
  `INVALID_CREDENTIALS` and no token is made.
- `lifetime` must be positive.
- `extraClaims` may not name the subject, the security stamp, `jti`, `nbf`, `exp`, `iss` or `aud`. Those are the library's.
- The token carries the subject, the current security stamp read from the store, a `jti` and `nbf`, and `exp` set to
  now plus `lifetime`. It is signed with `Key`, `Issuer` and `Audience` from `AuthOptions`.

The access pipeline accepts the token only when `Key`, `Issuer` and `Audience` match the ones the app validates.

## Single-use e-mail sign-in links

`AddBallastEmailLinks<TContext>` registers `EmailLinkService`. `IssueAsync(subjectKey, purpose, address, compose, ct)`
makes a link and sends it through `EmailService`; `RedeemAsync(secret, purpose, ct)` checks it and returns the subject key.

| Option (`AuthEmailLinkOptions`) | Default | Rule |
|---|---|---|
| `Lifetime` | 15 minutes | Between one minute and 24 hours. |
| `MaxIssuedPerWindow` | 5 | At least 1. More links than this in `RateWindow` answer `RATE_LIMITED`. |
| `RateWindow` | 1 hour | Greater than zero. |

- A link is single-use. Redeeming claims it with one conditional update (its hash, its purpose, unclaimed, not expired).
  Only one request can win.
- After the claim, the subject is checked against `IAuthAccountStore`. A missing or `CanSignIn = false` subject is refused
  with `EMAIL_LINK_INVALID`, and the link stays used. A deactivated subject's link cannot be redeemed again.
- The subject key follows the rule above: a prefixed key, such as `contact:42`, when the account is outside Identity.
- The rate limit counts issued links in the window before inserting. It is a brake against floods, not an exact cap: two
  requests in the same instant can both pass it.
- Link rows are kept whatever the send result, so a send that failed still counts against the limit.
- `PurgeAsync()` deletes the expired links and those older than the window. The hard delete is allowed, because link rows
  are short-lived secrets with no history.

## Google sign-in

`AddBallastGoogleSignIn` reads `AuthGoogleOptions` from the `Google` section (or `sectionName`).

| Option | Default | Meaning |
|---|---|---|
| `WebClientId` | null | The Google Web client id. The token's audience must be this id. Null leaves Google sign-in refused with `GOOGLE_SIGNIN_NOT_CONFIGURED`. |
| `NewAccountRole` | null | The role a first sign-in gets. Null gives none. |

`GoogleSignInService` behaves as follows.

- **`SignInAsync(idToken, rememberMe)`.** A known Google subject signs into its account. A first sign-in with a verified
  address creates a passwordless account with the role, the Google login and the app's rows (`IAuthNewAccount<TUser>`),
  in one transaction. A first sign-in whose address belongs to a password account is refused, audited on that account,
  and never linked. A race with a concurrent first sign-in signs into the account the other request made.
- **`LinkAsync(userId, idToken)`.** Links a Google account to a signed-in account. A Microsoft account is refused before
  Google is asked.
- **`UnlinkAsync(userId)`.** Removes the Google login. Refused when the account has no password, because it would have
  no way to sign in.
- A locked account is answered with `INVALID_CREDENTIALS`, the same answer the password path gives.
- The token's signature is checked by the `IGoogleTokenVerifier` (Google's library by default), for the `WebClientId`
  audience. The validator then checks what came back: the issuer must be Google's (`accounts.google.com`, in either form),
  the audience must be this app's, and the expiry must be in the future. A token that fails one of these is refused as not a
  Google token. The email-verified flag comes through, and the sign-in refuses an unverified address
  (`GOOGLE_EMAIL_UNVERIFIED`). An invalid token is `GOOGLE_TOKEN_INVALID`. A network failure is `GOOGLE_UNREACHABLE`, never a
  bad token.

## Microsoft staff sign-in

Staff sign in to an admin console with a Microsoft work or school account. One Entra app registration serves the console.
The setup steps are in [setup/microsoft-sign-in.md](setup/microsoft-sign-in.md).

### Registration and routes

`AddBallastMicrosoftSignIn` reads `MicrosoftSignInOptions` from the `MicrosoftSignIn` section and checks it at start. The
services are always registered. The OpenID Connect and cookie schemes are added only when `TenantId` and `ClientId` are
set, so an app without Microsoft sign-in has no Microsoft scheme at all. The directory sync's HTTP client is added only
when the client secret is set.

`MapBallastMicrosoftSignIn` maps three routes under `routePrefix`:

| Route | What it does |
|---|---|
| `GET {prefix}/start?login_hint=` | Challenges Entra. A hint that is an address at another company domain is refused before Entra is asked. A hint that is not an address is left out. |
| `GET {prefix}/complete` | Reads the handshake's cookie and decides it. Redirects to the sign-in page with `?code=` (a one-time code) or `?error=` (a refusal, with `&ms=` for Microsoft's own code when there is one). |
| `POST {prefix}/exchange` with `{ "code": "..." }` | Exchanges a one-time code for the session. Returns an `IssuedSession`: the access token, the refresh token and the refresh token's expiry. |

The body is `IssuedSession` (`accessToken`, `refreshToken`, `refreshTokenExpiresAtUtc`). It is not the `LoginResponse` an app may already use, which also carries a `user` object the library cannot know. An app that keeps that body calls `ExchangeAsync` from its own endpoint and maps the result. Every route is anonymous.

### Your own controller

An app with its own routes calls the same methods the library's routes call, so each decision is written once:

- `MicrosoftSignInStart.Decide(options, loginHint, completePath)` returns a `MicrosoftStartDecision`. When its `RedirectUrl`
  is set, redirect there. Otherwise challenge its `Scheme` with its `Properties`. `completePath` is the app's own complete
  route, which the challenge returns to.
- `MicrosoftSignInService.CompleteAsync(HttpContext, ct)` reads the handshake cookie, removes it in every case (a refusal, a
  success, and a sign-in that throws, since the removal is in a `finally`), and returns the sign-in page's address
  (`?code=` or `?error=`). Redirect to it. The library's complete route is one line of this.
- Every refusal of a Microsoft sign-in is logged at Warning, with its refusal code and the Microsoft object id. It never
  logs the address, a token or a name.

Both are public and need no database for `Decide`. `AddBallastMicrosoftSignIn` registers the service.

### Emergency accounts

An emergency account is a password account kept for the day Microsoft is down. The library cannot tell which accounts these
are, so the app says so: implement `IMicrosoftEmergencyAccounts<TKey>`, whose `IdsAsync(ct)` returns the keys of those
accounts, and register it. Either order against `AddBallastMicrosoftSignIn` works, and the app's registration is used. The
default names none.

- The directory sync leaves those accounts out. They are never switched off, never applied to, and not counted in the
  looks-wrong check. A person whose address is one of those accounts' is left out too, and no problem is listed for them.
- A Microsoft sign-in never links one of those accounts to a Microsoft login, and never changes it. A sign-in whose
  Microsoft login is already on one is refused with `MS_EMERGENCY_ACCOUNT`.

### Staff profile hook

An app that keeps its own row for each staff member, or that sets the account's key itself, implements
`IMicrosoftStaffProfile<TUser, TKey>` and registers it. Either order against `AddBallastMicrosoftSignIn` works, and the app's
registration is used. The default keeps nothing, so each call does nothing.

| Call | When it runs | If it throws |
|---|---|---|
| `CreatingAsync(user, person, ct)` | Before a new account is inserted, in the transaction that inserts it. The app may write its own row here, and set `user.Id` from it, so the account and the row share one key. A key the database generates is copied from the row after its save. | The account is not written, its role is undone, and the sign-in fails with `MicrosoftStaffProfileException`, whose inner exception is the app's. |
| `AppliedAsync(user, person, ct)` | After a name, address or role change of a linked account, in the transaction that makes it. A sign-in and the directory sync both call it. It does not run when only the switch-on changed. | The change is undone. A sign-in fails with `MicrosoftStaffProfileException`. The sync lists `PROFILE_HOOK_FAILED` for that person and goes on. |
| `SwitchedOffAsync(user, ct)` | After the directory sync switches an account off, in the transaction that switches it off. | The switch-off is undone for that person. The sync lists `PROFILE_HOOK_FAILED` and goes on. |

- A sync problem `PROFILE_HOOK_FAILED` has the person's address as its subject (the account's key for a switch-off), and the
  type of the app's exception as its detail, never its message.
- A key the app sets in `CreatingAsync` is kept as it is. The app's mapping must then say that the database does not
  generate that key.

### Marks for the app to show (MicrosoftStaffMarks)

`MicrosoftStaffMarks` is public so an app can show what the library wrote, read-only. An app reads these; it never writes them.

| Member | What it is | What the app may do with it |
|---|---|---|
| `SourceClaimType` (`source`) | The role claim type the library sets on a role it created from Microsoft. | Read the role's claims to find these roles. |
| `SourceMicrosoft` (`microsoft`) | The value of that claim on such a role. | Show the role as Microsoft's. Its permissions are still set in the app, and an app must not delete or rename it. |
| `IsSwitchedOff(lockoutEnd, now)` | True when an account's lockout is the directory sync's switch-off, rather than Identity's own failed-sign-in lockout. | Show the account as switched off. A failed-sign-in lockout is not reported here. |

Each member is a constant or a pure check, so reading it has no side effect.

### Options (MicrosoftSignInOptions)

| Property | Config key | Default | Meaning |
|---|---|---|---|
| `TenantId` | `TenantId` | empty | The Entra tenant id, a GUID. Empty turns Microsoft sign-in off. |
| `ClientId` | `ClientId` | empty | The Entra application id, a GUID. Empty turns Microsoft sign-in off. |
| `ClientSecret` | `ClientSecret` | empty | Used by the directory sync only. Empty leaves the sync off. |
| `SyncMinutes` | `SyncMinutes` | 15 | How often the app runs the sync. At least 5. The library does not schedule it. |
| `AllowedDomains` | `Domains` (`;`-separated) | empty | The company domains. When set, an address at another domain is refused. Empty allows every address. |
| `ConsolePrefix` | none | null | This console's prefix on role values, `Api` or `Map` in the defaults. Null means every value is a role name. Must be one of `ConsolePrefixes` and contain no dot. |
| `ConsolePrefixes` | none | `Api`, `Map` | The prefixes that name a console. A value with another console's prefix is ignored without a problem. |
| `RiderRole` | none | null | A role value that is the app's rider role. Listed as `RIDER_ROLE` and never a staff role. |
| `RoleMode` | none | `Many` | `Many`: every role of this console is a role of the account. `One`: exactly one is allowed; none is `MS_NO_ROLE`, several is `MS_MULTIPLE_ROLES`. |
| `FullAccessRole` | none | null | A role that gets a session without a permission claim, such as the owner's role. Null means every role needs a permission. |
| `PermissionClaimType` | none | `permission` | The claim type that marks a role as having a permission. |
| `CookieScheme` | none | `MicrosoftSignIn.Cookie` | The cookie that carries the handshake's result to `complete`, for five minutes. |
| `Scheme` | none | `Microsoft` | The OpenID Connect scheme name. |
| `CallbackPath` | none | `/signin-oidc` | The callback Entra returns to. Must start with a slash. |
| `LoginPagePath` | none | `/admin/login.html` | The sign-in page that refusals and codes are redirected to. Must start with a slash. |

Start-up refuses a `TenantId` or `ClientId` that is not a GUID, a `SyncMinutes` under 5, a `ConsolePrefix` that is not
in `ConsolePrefixes` or has a dot, and a path without a leading slash.

### Refusals

The sign-in page shows one sentence per refusal. The redirect's `error` query carries the refusal:

| Refusal | Meaning |
|---|---|
| `MS_NOT_CONFIGURED` | The tenant or the client is empty. |
| `MS_NOT_COMPANY_ADDRESS` | The address is not at one of `AllowedDomains`. |
| `MS_SIGNIN_FAILED` | The handshake failed, or the token lacks a usable object id or address. |
| `MS_WRONG_TENANT` | The token is from another tenant than `TenantId`. |
| `MS_NO_ROLE` | No role of this console. |
| `MS_MULTIPLE_ROLES` | `RoleMode` is `One` and the person has several roles. |
| `MS_NAME_MISSING` | The given name or the family name is missing. Names are never split out of the display name. |
| `MS_ROLE_NO_PERMISSIONS` | The role exists but no permission is set on it. The account and its roles are kept. |
| `MS_ADDRESS_TAKEN` | A password account holds the address, or another Microsoft account does. It is never linked. |
| `MS_EMERGENCY_ACCOUNT` | The Microsoft login is on an emergency account (`IMicrosoftEmergencyAccounts<TKey>`). It is never changed, so the sign-in is refused. |

A one-time code that fails at the exchange is `SIGNIN_CODE_INVALID`, a status 400 `AppException`.

### Sign-in, codes and the recheck

- **Account matching.** An account is matched by the Microsoft object id, stored as a Microsoft login. A new person's
  account is created with the address as its sign-in name, no password, the roles and the login, in one transaction.
  A password account that holds the address is refused with `MS_ADDRESS_TAKEN` and audited on that account, never linked.
- **One-time codes.** A code is 32 random bytes, base64url, shown once. The database keeps its SHA-256 in the Identity
  token table, under the login provider `MicrosoftSignInCode`, with its expiry. It lives 60 seconds. One live code is kept
  per account: an earlier one is removed when a new one is issued.
- **Exchange.** The row is claimed by a conditional delete on its hash. Two exchanges of one code race, and exactly one
  wins; the other is `SIGNIN_CODE_INVALID`. After the claim the expiry is checked, and the account is checked again: it must
  still exist, still have a Microsoft login, not be switched off by the sync, and not be locked. A code whose account
  changed since it was issued is refused with the same answer. The session is issued with `remember-me` off.

### The directory sync

`MicrosoftDirectorySync.RunOnceAsync(ct)` returns a `MicrosoftSyncResult` with an outcome:

| Outcome | What happened | Written |
|---|---|---|
| `Off` | The client secret or the tenant is missing. | Nothing. Nothing is read. |
| `NotHolder` | This copy of the app does not hold the job lock. | Nothing. Checked before the read and again before the write. |
| `ReadFailed` | Microsoft could not be read. The result carries the `DirectorySyncFailure` and the HTTP status. | Nothing. |
| `LooksWrong` | The run would switch off every linked account while some were on. | Nothing. Logged as an error. |
| `Applied` | The changes were written in one transaction. | Everything in the plan. |
| `WriteFailed` | A write failed. The transaction was rolled back. | Nothing. |

A staff profile hook that throws undoes only that person's change, lists `PROFILE_HOOK_FAILED` for them, and the run goes on
to the rest. Such a run is still `Applied`. See "Staff profile hook" above.

**The job holder.** The sync needs `IJobLeadership` from Ballast.Jobs, so only the copy that holds the job lock writes.
Schedule it with the job host (`AddLeaderHostedService` in Ballast.Jobs, see [Jobs.md](Jobs.md)), every `SyncMinutes`.
The library never schedules it itself. The app must register the job lock before it resolves the sync.

**What the plan decides.** The plan is pure (`MicrosoftSyncPlan.Plan`), so each rule is tested alone:

- An app role value is read for this console (the prefix rule above). A value that cannot be used is listed once.
- A person whose console roles are none, or who is disabled in Microsoft, is switched off when they have a linked account.
  A linked account whose person is not in the directory any more is switched off too.
- In `One` mode, a person with several roles is listed as `MULTIPLE_ROLES` and left as they are.
- A person with no name, or no address, is listed and left as they are.
- An address held by another account is listed as `ADDRESS_TAKEN`. The account is updated, but keeps its old address.
- A role that Microsoft gave and no longer does is listed as `ROLE_NOT_IN_MICROSOFT`. The role is never deleted. The same
  list is on the result as `RolesNotInMicrosoft`, so an admin page can show it without reading the problems.
- The sync never creates an account. The first sign-in does that.
- Emergency accounts are left out entirely: not switched off, not applied, not counted in the looks-wrong check.

**Applying a change.** A changed name, address or role ends the account's sessions: the security stamp changes, so the
access tokens it signed fail at the next request, and its refresh tokens are revoked. A switch-off removes the roles,
locks the account until a later sync switches it on, and ends its sessions.

**Graph.** `MicrosoftGraphClient` reads the directory over Microsoft Graph with the client-credentials grant:

- At most `MicrosoftGraphClient.MaxPages` (20) pages per list. A list with more is a read failure, never a partial read.
- A next link is followed only when it is an `https` link on `graph.microsoft.com`, because the token goes with it.
- A throttled answer (429 or 503) is retried once after its `Retry-After`, capped at 60 seconds. Without a `Retry-After`,
  the wait is 10 seconds.
- A request has a 30-second timeout (the typed HTTP client).
- The read fails with a `MicrosoftGraphException` that names no token and no secret. The exception's `Failure` is one of
  `Token`, `Graph`, `Timeout`, `Response` or `Unexpected`.

## Failure and fallback

- An unknown subject, an expired token, a refused code and a locked account all get one answer, so none reveals which
  case it is.
- A failed Google or Microsoft call is a visible refusal (`GOOGLE_UNREACHABLE`, a `ReadFailed` sync), never a stored answer
  used in its place.
- A sync that cannot read, or would leave nothing on, writes nothing. The last applied state stays.
- Every catch in the library logs or rethrows. The sync logs each problem it lists.

## Known limits

- The library's tests cover the one-time code race, the recheck at the exchange, code expiry, the address-taken and
  domain refusals, the looks-wrong stop, a sync that cannot read, the prefix rule, the domain check, the emergency
  account, single-role mode, retired roles, the e-mail link recheck and the subject key. They also cover the two
  `PasswordSignInBy` modes, the admin password reset, and the staff profile hook: its three calls, the create rollback,
  the per-person rollback in a sync, and a key the app sets.
- The reset's audit row names no administrator, and a reset does not lift a lockout.
- With `RequireUniqueEmail` off, Identity checks no address, so the library registers `AddressUniquenessValidator` beside
  Identity's own validators. It allows an account with no address, and refuses an address another account already has,
  on create and on a change, compared on the normalised address, with Identity's `DuplicateEmail`. So the library's
  address lookups (`FindByEmailAsync`, in the Microsoft and Google sign-ins and the sync) never meet two accounts. This
  holds only for accounts written through the library's Identity validators; rows written around them (a raw SQL insert,
  or an app's own store) are not checked, and an app that writes those must keep addresses unique itself.
- `MicrosoftSignInService` and `MicrosoftDirectorySync` take the profile hook as a new constructor parameter, and
  `MicrosoftSignInService` also takes a logger. `GoogleIdTokenValidator` takes the verifier. An app that builds these by
  hand, rather than from dependency injection, must pass them.
- They do not run the HTTP routes end to end, and they do not test the Graph client's paging or throttling against a
  live or fake HTTP server. Those are reviewed, not yet tested.
- `IssuedSession` is the exchange's body. An app's `LoginResponse` may also carry a `user` object, so an app that keeps that body calls `ExchangeAsync` from its own endpoint.
- The Microsoft sign-in does not link an existing password account, by address or otherwise. Moving a person from one
  tenant to another needs the app to remove the old Microsoft login first; the library has no call for that.
