# @ballast/web

Browser helpers for pages that the server renders: escaping for HTML text and attributes, a safe reader for the server's
instants, the one-line text for a failed answer, a page translation loader, and place-time formatting.

There is no build step. Each file is a classic `<script>`, the way the pages already load their code. A file installs its
functions on `window.BallastWeb`, and the zone-time file installs its functions as globals, which is how map pages call them.
Nothing is loaded from a CDN, so the page can keep a strict Content-Security-Policy: copy the files into the app's own
`wwwroot` (see `scripts/vendor-copy.mjs` in the Ballast repository).

Install (the npm package, for the copy step):

```
npm install @ballast/web@0.1.0-preview.2
```

## Example

```js
// Node: the same functions, exported for tests and build scripts.
const { escapeHtml } = require('@ballast/web/browser/html.js');
const { utcInstant } = require('@ballast/web/browser/utc.js');

console.log(escapeHtml('<a title="x">Čakovec & co</a>'));
console.log(utcInstant('2026-10-01T10:05:00').toISOString());
console.log(utcInstant('2026-11-03') !== null);
```

The example prints `&lt;a title=&quot;x&quot;&gt;Čakovec &amp; co&lt;/a&gt;`, then `2026-10-01T10:05:00.000Z`, then `true`.

In a page, load the files in this order and read the namespace:

```html
<script src="/vendor/ballast/web/html.js"></script>
<script src="/vendor/ballast/web/utc.js"></script>
<script src="/vendor/ballast/web/problem.js"></script>
<script>
  const { escapeHtml, utcInstant, problemMessage } = window.BallastWeb;
</script>
```

The usage guide, with every function, option and failure, is [`docs/Web.md`](../../docs/Web.md) in the repository.
