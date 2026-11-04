# Ballast.Admin: usage guide

Ballast.Admin is the kit for a staff console: the pages an operator uses to run an app. It has two halves that work together.

- **The server half** is the .NET package `Ballast.Admin`. It gives the console's static files a content security policy, a cache
  rule and headers, and it answers "who am I" so the console can show the pages a person may use.
- **The browser half** is `@ballast/admin`, the npm package in `js/admin`. It holds the sign-in state, the page shell (rail, top
  bar, pager, error banner, counts), the sign-in page and a staff-and-roles page. It runs on `@ballast/web`, and its guide is
  [Web.md](Web.md) for the shared helpers.

The package READMEs have examples that run as written. This guide covers every option and every way each part fails.

## The server half: Ballast.Admin

### AdminConsoleOptions

| Property | Default | Meaning |
|---|---|---|
| `Path` | `/admin` | The path of the console's static files. The rule applies to the path and everything under it, by segment, so `/admin` covers `/admin/x.js` and not `/administrator`. The comparison ignores case. Must start with `/`. |
| `CacheControl` | `no-cache` | The `Cache-Control` header of the files. `no-cache` allows a cache but makes it check the file first, so a deploy takes effect at once. |
| `AllowInlineStyles` | true | Whether the style sheets may hold inline styles (`'unsafe-inline'` in `style-src`). The consoles write inline `style` attributes. Scripts are never allowed inline unless `AllowInlineScripts` is set. |
| `AllowInlineScripts` | false | Whether scripts may be written inline (`'unsafe-inline'` in `script-src`, written first). **This is weaker than the default, and a console should keep it off.** Markup the page renders could then run as script if its escaping ever failed, so the policy no longer backs the escaping up. Turn it on only for a console whose own pages need inline script, and say why in the app. |
| `ScriptSources` | empty | Extra origins for `script-src`, after `'self'` (and after `'unsafe-inline'` when `AllowInlineScripts` is on). Each origin named here can run code in the console. `'unsafe-inline'` and `'unsafe-eval'` are refused here (`ArgumentException`): inline script is turned on only with `AllowInlineScripts`, and eval is never allowed. |
| `StyleSources` | empty | Extra origins or keywords for `style-src`, after `'self'`. |
| `FontSources` | empty | Extra origins or keywords for `font-src`, after `'self'`. |
| `ImageSources` | empty | Extra origins or keywords for `img-src`, after `'self' data:`. |
| `ConnectSources` | empty | Extra origins or keywords for `connect-src`, after `'self'`. |
| `WorkerSources` | empty | Extra origins or keywords for `worker-src`, after `'self'`. The directive is written only when this list is not empty; without it, workers follow `script-src`, which is `'self'` by default. |

Every source an app adds is one more origin the page may load from, so name each on purpose. A source must not be blank, hold
white space, hold a `;` or a `,`, or be `*`.

The defaults write this policy, and a test pins it byte for byte:

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'
```

The directives are always written in this order: `default-src`, `script-src`, `style-src`, `img-src`, `connect-src`, `font-src`,
`worker-src` (only when set), `frame-ancestors`, `base-uri`, `form-action`. The order is fixed so the default output never moves.
A policy written by hand in another order (for example with `font-src` before `img-src`) has the same directives and sources,
and a browser reads both the same way; the text differs only in that order.

### AdminConsole

- `Rule(Action<AdminConsoleOptions>? configure = null)` returns a `StaticFileRule` built from the options. It sets the content
  security policy, the cache control, and three headers: `X-Robots-Tag: noindex, nofollow`, `X-Content-Type-Options: nosniff`
  and `Referrer-Policy: no-referrer`.
- `AddAdminConsole(this SecurityHeaderOptions options, Action<AdminConsoleOptions>? configure = null)` adds that rule to the
  options and returns them, so it chains inside `AddSecurityHeaders`.
- `ContentSecurityPolicy(AdminConsoleOptions options)` returns the policy string. With the defaults it is:

  ```
  default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'
  ```

  The directives are always in this order. `script-src` is always `'self'` and has no option, so the policy never allows an
  inline script, `'unsafe-eval'` or a wildcard. `frame-ancestors 'none'` stops the console being framed.

Setup, with the Google Fonts sources an app might use:

```csharp
builder.Services.AddSecurityHeaders(headers => headers.AddAdminConsole(console =>
{
    console.StyleSources.Add("https://fonts.googleapis.com");
    console.FontSources.Add("https://fonts.gstatic.com");
}));

app.UseSecurityHeaders();
app.UseSecureStaticFiles();
```

`UseSecurityHeaders` writes the headers every response carries, and `UseSecureStaticFiles` serves the web root with the rules
applied. Rules are applied only through `UseSecureStaticFiles`. A console served by a plain `UseStaticFiles` gets none of the
headers, so the app must use `UseSecureStaticFiles` for its console.

Rules are applied in the order they were added, and a later rule overwrites a header an earlier one set. Add the console's rule
before any page-specific rule that should win. A file that no rule matches keeps the default content security policy that
`UseSecurityHeaders` writes (`default-src 'none'; frame-ancestors 'none'; base-uri 'self'` unless the app changed it).

Failures:

| Call | Failure |
|---|---|
| `Rule` with a blank `CacheControl` | `ArgumentException`, parameter `CacheControl`. |
| `Rule` with a `Path` that is blank or does not start with `/` | `ArgumentException`, parameter `path`, from the static file rule. |
| A source that is blank, holds white space, a `;` or a `,`, or is `*` | `ArgumentException`, parameter named for the list (`StyleSources`, `FontSources`, `ImageSources` or `ConnectSources`). |
| `ContentSecurityPolicy(null)`, `AddAdminConsole` on null | `ArgumentNullException`. |
| `AddSecurityHeaders` with a blank header value | `InvalidOperationException`, from Ballast.AspNetCore. The console's own values are never blank. |

### AdminWhoAmI

`AdminWhoAmI.Create(ClaimsPrincipal principal, AdminWhoAmIOptions? options = null)` returns an `AdminWhoAmIResponse`, or null
when the principal is not authenticated. An app maps it with a thin endpoint:

```csharp
app.MapGet("/admin/api/me", (ClaimsPrincipal user) =>
{
    var me = AdminWhoAmI.Create(user, new AdminWhoAmIOptions { FullAccessRole = "Admin" });
    return me is null ? Results.Unauthorized() : Results.Ok(me);
});
```

The endpoint sits behind the app's own sign-in. A null answer is a 401.

`AdminWhoAmIOptions`:

| Property | Default | Meaning |
|---|---|---|
| `SubjectClaim` | `sub` | The claim with the person's id. |
| `NameClaim` | `name` | The claim with the display name. |
| `EmailClaim` | `email` | The claim with the email address. |
| `RoleClaim` | `role` | The claim with the roles, one claim per role. |
| `PermissionClaim` | `permission` | The claim with the permissions, one claim per permission. |
| `FullAccessRole` | null | The role that gives full access, matched without case. Null means no role does. |

The defaults are the names a token keeps when inbound claim mapping is off. An app whose pipeline maps claims (for example
`MapInboundClaims` left on) sets the mapped names, such as `ClaimTypes.Role`.

`AdminWhoAmIResponse`, serialized as JSON with camelCase names:

| Property (JSON) | Meaning |
|---|---|
| `Subject` (`subject`) | The person's id, or null when the principal has no subject claim. |
| `Name` (`name`) | The display name, or null. |
| `Email` (`email`) | The email address, or null. |
| `Roles` (`roles`) | The roles, distinct and sorted by ordinal comparison. Empty when there are none. |
| `Permissions` (`permissions`) | The permissions, distinct and sorted by ordinal comparison. Empty when there are none. |
| `FullAccess` (`fullAccess`) | True when the person holds the `FullAccessRole`. |

The answer is for navigation only. The console uses it to decide which pages to show. Every action on the server still checks
its own permission, so a wrong answer here shows a page the person cannot use, and cannot let them use it.

Failures:

- A principal that is not authenticated gives null. A principal with claims but no authentication type is not authenticated.
- A claim type in the options that is blank throws `ArgumentException`, with the option's name as the parameter.
- A null principal throws `ArgumentNullException`.
- Missing claims give null fields and empty lists, never an error. Blank claim values are ignored.

## The browser half: @ballast/admin

### Loading it

`@ballast/web` loads first, then the admin files, all as classic scripts in one window:

```html
<script src="/vendor/ballast/web/html.js"></script>
<script src="/vendor/ballast/web/utc.js"></script>
<script src="/vendor/ballast/web/problem.js"></script>
<script src="/vendor/ballast/admin/auth.js"></script>
<script src="/vendor/ballast/admin/shell.js"></script>
<script src="/vendor/ballast/admin/login.js"></script>
<script src="/vendor/ballast/admin/roles.js"></script>
```

Each file installs its functions on `window.BallastAdmin`. Under Node the same files are CommonJS modules. If `window.BallastWeb`
is missing when the shell needs it, the shell throws `@ballast/web must be loaded before @ballast/admin: window.BallastWeb is missing.`
The files are copied into the app with `scripts/vendor-copy.mjs` (see [Web.md](Web.md#vendor-copies-and-the-byte-identity-check)).

### createAuth(options): the sign-in state

Returns `token()`, `setTokens(tokens)`, `clearTokens()`, `headers()`, `claims()`, `toLogin()`, `signOut()`,
`requireSignedIn()`, `refresh()`, `install(installOptions)`, `storage()`, `loginPath`, and `keys`.

| Option | Default | Meaning |
|---|---|---|
| `storage` | `sessionStorage` | Where the tokens are kept. The tokens live only for the browser tab. |
| `location` | `window.location` | The page's location. |
| `fetch` | the page's `fetch`, as it is when `createAuth` runs | The function the refresh and the sign-out use. It is captured once, and `install()` never changes it, so a refresh is never wrapped and never renews itself. |
| `tokenKey` | `auth_token` | The storage key of the access token. |
| `refreshKey` | `refresh_token` | The storage key of the refresh token. |
| `redirectKey` | `redirect_after_login` | The storage key of the page to return to after sign-in. |
| `loginPath` | `/admin/login.html` | The sign-in page. |
| `loginPageSuffix` | `/login.html` | A page whose path ends with this is never redirected to sign-in. |
| `refreshPath` | `/auth/refresh` | The refresh endpoint. It takes `{ refreshToken }` and answers `{ accessToken, refreshToken }`. |
| `refreshTimeoutMs` | 15000 | How long a refresh may take before it counts as failed. The same bound applies to the sign-out request. |
| `logoutPath` | `null` | The sign-out endpoint. When set, `signOut()` posts `{ refreshToken }` to it (JSON, `keepalive`, through the captured `fetch`), so the server can end the session. Null means `signOut()` only clears the tokens. |

How it behaves:

- **One refresh at a time.** The refresh token rotates: the server accepts it once. Concurrent callers share one refresh request,
  and every caller waits for it. The request is bounded by `refreshTimeoutMs`, so a stalled request cannot hold every later
  401 until a reload.
- **A refused refresh fails at once.** The refresh and the sign-out use the `fetch` captured when `createAuth` runs, not the
  page's `fetch` after `install()`. A refused refresh resolves false straight away; it never waits for its own answer through
  the wrapper.
- `refresh()` resolves to false when there is no refresh token, the server refuses it, the answer is not a token pair, or the
  request times out. A failed refresh does not clear the tokens by itself; the caller decides.
- `install({ redirectWhenSignedOut })` wraps `window.fetch` for the console's own origin only. The bearer token is attached to
  each such request. A 429 is retried once after its `Retry-After` (2 seconds when the header is missing or not a number, at
  most 5 seconds). A 401 renews the token once and retries the request. A renewal that fails clears the tokens and sends the
  person to sign in. Tokens never go to another origin. With `redirectWhenSignedOut`, a page opened with no token goes to
  sign-in at once, except the sign-in page itself.
- `toLogin()` keeps the current address under `redirectKey` and opens the sign-in page.
- `signOut()` clears both tokens and opens the sign-in page. Nothing is kept for a later visit. With `logoutPath` set and a
  refresh token stored, it first posts the refresh token there, with `Authorization: Bearer <access token>` when an access token
  is stored (so a logout endpoint that needs sign-in ends the session), and waits for the answer, at most `refreshTimeoutMs`. The tokens
  are cleared and the sign-in page is opened whatever the answer or a network failure. It returns a promise of `true` when the
  server confirmed the sign-out (or there was nothing to send: no `logoutPath`, or no refresh token), and `false` when it did not.
  The promise is always returned; without `logoutPath` the tokens are cleared at once, as before.
- `requireSignedIn()` returns true when a token exists. Without one it calls `toLogin()` and returns false.
- `setTokens({ accessToken, refreshToken })` throws a `TypeError` without an access token. A refresh token is kept only when one is given.
- `decodeClaims(token)` reads the payload of a JWT without checking its signature. It returns null for anything that is not
  one. The server checks every request; the claims are for display.

### createShell(options): the page shell

Draws the rail (the navigation), the top bar and the content area, and gives the page its helpers. Requires `auth`, from
`createAuth`; without it the call throws a `TypeError`.

| Option | Default | Meaning |
|---|---|---|
| `auth` | required | The sign-in state. |
| `nav` | `[]` | The navigation groups: `[{ label, items: [...] }]`. |
| `brand` | `{}` | The rail's `mark`, `name` and `sub` text. |
| `footerHtml` | none | Markup for the rail's footer. It is written as it is, so it must be trusted markup, not data. |
| `showEnvironment` | true | Draws the LOCAL or PRODUCTION badge. |
| `environmentFor` | LOCAL for `localhost`, `127.0.0.1` and `[::1]`, else PRODUCTION | Gives the badge for a host name. |
| `counts` | none | A function that reads the counts as `{ key: number }`. It is given the shell. |
| `countsDelayMs` | 250 | How long after the page opens the counts are read. |
| `mePath` | none | The endpoint that answers `{ permissions, fullAccess }`, such as the `AdminWhoAmI` endpoint. |
| `problem` | `{}` | The options `problemMessage` takes (`fieldErrors`, `traceId`, `codeText`, `preferDetail`, `fallback`), passed on every error banner the shell writes. The defaults give the full text. An app that keeps its own banner text passes `{ fieldErrors: false, traceId: false }`. For `httpErrorMessage` a given `fallback` replaces `HTTP <status>`. |
| `highlightHash` | true | Brings the element named by the address's hash into view. |
| `window`, `document`, `storage`, `fetch` | the page's | The browser objects. `storage` (sessionStorage) keeps the counts for the next page. |
| `countsKey` | `_shellCounts` | The storage key of the counts. |

A navigation item has `key`, `text` and `href`, and may have:

- `count`: a `data-count` key, drawn as a number;
- `badge` or `pill`: a `data-count` key, drawn only when the count is not zero;
- `pills`: several of those, each `{ count, cls, title }`;
- `live`: a dot;
- `permission`: one permission the person must have;
- `anyPermission`: an array of permissions, one of which the person must have.

Permissions. Until the permissions are known, every item is drawn. `loadPermissions()` reads `mePath` and redraws the rail
with the answer; a failed read rejects and the rail stays as it was. A person with full access sees every item.
`setPermissions(list, everything)` sets them directly, and `can(permission)` checks one.

The shell returns:

- `mount(page)` draws the rail and top bar around an empty `#content`, which the page fills. `mountLegacy(page)` draws them around
  a page that already renders its own markup, moving that markup inside. It returns false when there is no `#page` element.
- `api(path, init)` calls the console's API with the token. A 204 gives null. A failed answer throws an `Error` whose message is the
  server's own words (`detail`, else `title`, with field messages appended) and whose `status` is the HTTP status. A 401 renews
  the session once and retries; a renewal that fails signs out and throws `Session expired.`. A 429 with a `Retry-After` of
  1 to 10 seconds waits `min(Retry-After, 3)` seconds and retries once; a missing `Retry-After` counts as 2 seconds. A
  `Retry-After` of 0, one that is not a number, or one over 10 seconds is not retried and throws
  `Too many requests — retry in Ns`.
- `tryApi(path)` is `api` that gives null on any failure. Use it only where a null answer is an honest result, because it hides
  the reason.
- `httpErrorMessage(status, body)` gives the one line of text for a failed answer, with `HTTP <status>` when the answer has
  nothing to say.
- `showError(message)` shows the message in the `#error` banner and hides the spinner in `#loading`. With no `#error` element it
  writes the message into `#content`, so it is never lost.
- `fail(message)` writes a message into `#content`.
- `renderPagination(state, onPage)` draws a Bootstrap pager into `#pagination`, or `state.container`. `state` is
  `{ page, totalPages, hidden, summary, container }`. `hidden` hides the pager when there is nothing to page through, and
  `onPage(n)` is called with the page the operator chose.
- `applyCounts(counts)`, `restoreCounts()`, `fetchCounts()` draw the counts into `data-count` elements. The counts are kept in
  storage for the next page, merged with the new ones. A count that is null keeps the figure already drawn, because its read failed.
- `highlightHash()` brings the hash's element into view.
- `esc(value)` escapes text (`@ballast/web`'s `escapeHtml`). `safeUrl(value)` checks a link (`safeUrl` from `@ballast/web`).
- `num(value)`, `date(value)`, `dateTime(value)`, `ago(value)`, `initials(text)` format values for display. An empty instant shows a dash.

Failures to know about:

- A page that calls `api` and gets a non-JSON success answer gets a `SyntaxError` from `res.json()`. The shell does not guess.
- The counts come from `counts()`. A count that cannot be read keeps its last drawn value and is not shown as zero.
- `footerHtml` is written as given, so it must be trusted markup. The brand text and the navigation text are escaped.

### createLoginPage(options): the sign-in page

`createLoginPage({ auth, ... })` returns `{ start() }`. Call `start()` once, when the page loads. It wires a password form, a
Microsoft button, the return from Microsoft and the way back to the page the operator came from.

| Option | Default | Meaning |
|---|---|---|
| `auth` | required | The sign-in state. |
| `ids` | `loginForm`, `email`, `password`, `reveal`, `submit`, `error`, `microsoft` | The element ids of the form, the email field, the password field, the show-password button, the submit button, the error line and the Microsoft button. Each may be replaced one by one. |
| `identifierField` | `email` | The request field for the email address. |
| `loginPath` | `/auth/login` | The password sign-in endpoint. |
| `microsoftStartPath` | `/auth/microsoft/start` | Starts the Microsoft sign-in. |
| `microsoftExchangePath` | `/auth/microsoft/exchange` | Exchanges Microsoft's one-time code for a session. |
| `homePath` | `/admin/` | Where to go after sign-in when there is no back-link. |
| `adminPrefix` | `/admin/` | The only path prefix a back-link may name. |
| `loginPageSuffix` | `/login.html` | The sign-in page's file name, never a back-link target. |
| `messages` | the defaults | Replaces the Microsoft refusal texts, one by one. An object, or a function that returns one; a function is asked each time a refusal is drawn. |
| `text` | the defaults | Replaces the page's wording, one by one. An object, or a function that returns one; a function is asked each time the page draws text, so the wording follows the language. |
| `languageEvent` | `languageChanged` | The event that draws the page's text again. The `@ballast/web` loader dispatches `languageChanged` on the window when the language changes, with the new language in `detail.lang`. Null turns the redraw off. |
| `eventTarget` | the window | What `languageEvent` is listened for on. |
| `document`, `location`, `history`, `fetch` | the page's | The browser objects. |

The page must supply, in its markup:

- the show-password button (`reveal`), which is a button with an `aria-pressed="false"` attribute to start. Its state (`aria-pressed`),
  its label (`aria-label` and `title`) and, when the button has one, its first element child (an icon, whose `class` and size
  the page sets) are updated on each click and on a language change. The button may have no child: then only its state and label
  change.
- the submit button (`submit`), whose label is set to the sign-in text, or the signing-in text while a request is in flight.
- the error line (`error`), which the page hides with the `hidden` class until there is a message.

Behaviour:

- **Back-link.** `redirectTargetFor(storage, options)` reads the address the operator came from (under `redirectKey`) and
  removes the key in every case. It is honoured only when it is a same-origin address under the admin prefix and not the
  sign-in page. Anything else goes to `homePath`, so a stale or edited value cannot send the operator off the site.
- **Microsoft's return.** Microsoft comes back with `?code=` (a one-time code, exchanged for a session) or `?error=` (a refusal to
  show). The query is cleared at once, so a reload never repeats either. A refusal is shown from `microsoftErrorText(code,
  providerCode, messages, text)`. Only the known refusal codes get their own text, and `ms=` (Microsoft's own code) is shown
  only when it has the shape of an `AADSTS` number or an OAuth error word. Anything else gets the generic line.
- **Microsoft's button** is a full-page navigation, not a form post, because the console's policy allows `form-action 'self'`
  only. The address typed in the email field goes along as `login_hint`, so Microsoft's page can pre-fill the same account.
- **Password sign-in** posts the email (under `identifierField`) and the password as JSON to `loginPath`. A 400 or 401 shows the
  invalid-credentials line; any other failed answer shows the sign-in failed line with its status; a network failure shows the
  network line. An answer with no access token is a failure. A success stores the tokens through `auth.setTokens` and goes to
  the back-link.
- **Microsoft's return** exchanges the code with `microsoftExchangePath`. A failed exchange shows the expired line.

Failures:

- `createLoginPage` without `auth` throws a `TypeError`.
- A back-link that is not valid is ignored silently, and the operator lands on `homePath`.
- A Microsoft refusal code that the page does not know shows the generic line, not the code.

### createRolesPage(options): the staff and roles page

`createRolesPage({ shell, adapter, page, ... })` returns `{ start() }`, which loads the roles and the permission list and draws
them. Each role is a card with one checkbox per permission and a save button. A role the directory no longer lists can be
deleted when the adapter offers `deleteRole`.

The page talks to the server only through the adapter, so the same page serves a console whose roles are named and one whose
roles are numbered:

```
listRoles()            -> [{ key, name, permissions: string[], fromMicrosoft?: boolean }]
listPermissions()      -> [{ key, displayName, description, category }]
saveRole(key, perms)   -> resolves when the permissions are saved
deleteRole(key)        -> optional; resolves when the role is deleted
syncStatus()           -> optional; the directory sync's snapshot, or null when it cannot be read
runSync()              -> optional; runs the sync and returns its snapshot
```

A console with no directory sync leaves out `syncStatus` and `runSync`, and the sync panel is not drawn.

| Option | Default | Meaning |
|---|---|---|
| `shell` | required | The shell from `createShell`. |
| `adapter` | required | The data adapter above. It must have `listRoles` and `saveRole`. |
| `page` | required | The top bar: `{ active, title, crumb?, meta?, actions? }`. |
| `riderRole` | null | The role the app sets, shown read only. Null for none. |
| `fullAccessRole` | null | The role that passes every permission. Null for none. |
| `text` | the defaults | The page's wording, one by one. |
| `permissionNames` | none | The names of the permissions the page needs, for the 403 message. |
| `results`, `failures`, `secretStates` | the defaults | The names of the sync's results, failures and client secret states, one by one. |
| `document` | the page's | The browser document. |

Failures:

- A missing `shell` or `adapter` (with `listRoles` and `saveRole`) throws a `TypeError` when the page is created.
- A failed load or save shows the server's words through the shell. A 403 names the permission the page needs, because the
  server's answer for it has no body of its own.
- A role that the directory still lists cannot be deleted from the page; the delete control is offered only for a role the
  directory no longer lists.

The helpers `sortRoles(roles, riderRole, fullAccessRole)`, `explainError(error, permission, labels)`, `roleCard`,
`permissionBoxes`, `syncBody` and `syncPanel` are installed too, for a page that draws its own parts.

## What the console does not do

- It does not decide who may use the console. The server decides, with the claims and permission checks of the app. The
  navigation shows a page to a person the server may refuse; the refusal is then the answer.
- It does not keep tokens past the browser tab. A tab that closes signs the person out.
- It does not write the app's text. The page's wording comes in through `text` options, with English defaults.
