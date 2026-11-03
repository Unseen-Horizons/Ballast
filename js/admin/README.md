# @ballast/admin

The staff console for a Ballast app, in the browser. It has four parts, and each is a classic script that installs one function
on `window.BallastAdmin`:

- **Shell** (`createShell`): the rail with its navigation, the top bar, the API call helper, the pager, the error banner, and the
  counts on the navigation. Navigation items may need a permission.
- **Sign-in state** (`createAuth`): the tokens in sessionStorage, one shared refresh (the server treats a second use of a refresh
  token as theft), and the bearer token on the console's own requests.
- **Sign-in page** (`createLoginPage`): a password form, a Microsoft button, the return from Microsoft, and the way back to the page
  the operator came from.
- **Staff and roles page** (`createRolesPage`): the roles, their permissions, and the directory sync, behind a data adapter, so a
  console with named roles and one with numbered roles use the same page.

It needs [`@ballast/web`](../web/README.md) loaded first, for escaping and instants. Nothing is loaded from a CDN, so the page keeps
a strict Content-Security-Policy: copy the files into the app's `wwwroot` with `scripts/vendor-copy.mjs`.

Install (the npm package, for the copy step):

```
npm install @ballast/admin@0.1.0-preview.2
```

## Example

```js
// Node: the functions are exported, which is how the tests use them.
const { redirectTargetFor } = require('@ballast/admin/browser/login.js');

const stored = { value: '/admin/feeds.html?x=1', getItem() { return this.value; }, removeItem() { this.value = null; } };
console.log(redirectTargetFor(stored, { redirectKey: 'redirect_after_login', origin: 'https://console.example.test',
  adminPrefix: '/admin/', loginPageSuffix: '/login.html', homePath: '/admin/' }));
```

The example prints `/admin/feeds.html?x=1`. A stored address off the console, or the sign-in page itself, prints `/admin/` instead.

In a page, load `@ballast/web` first, then this package, then the console's own script:

```html
<script src="/vendor/ballast/web/html.js"></script>
<script src="/vendor/ballast/web/utc.js"></script>
<script src="/vendor/ballast/web/problem.js"></script>
<script src="/vendor/ballast/admin/auth.js"></script>
<script src="/vendor/ballast/admin/shell.js"></script>
<script src="/admin/console.js"></script>
```

The usage guide, with every option, function and failure, is [`docs/Admin.md`](../../docs/Admin.md) in the repository.
