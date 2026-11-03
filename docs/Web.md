# @ballast/web: usage guide

@ballast/web is the browser helpers that server-rendered and static pages share: escaping for markup and URLs, the instant a
server string means, reading an error answer into one line of text, a translation loader, and the time helpers that show a
place's time in the place's zone. It is plain JavaScript that a page loads with `<script src>`. It has no bundler, no
transpiler and no dependency.

The package README has the install line and an example that runs as written. This guide covers every function and every way
each one fails.

## Loading it in a page

Pages load the files as classic scripts. The first four install their names on `window.BallastWeb`:

```html
<script src="/vendor/ballast/web/html.js"></script>
<script src="/vendor/ballast/web/utc.js"></script>
<script src="/vendor/ballast/web/problem.js"></script>
<script src="/vendor/ballast/web/i18n.js"></script>
<script src="/vendor/ballast/web/zone-time.js"></script>
```

`zone-time.js` is the exception. Its functions are globals (`fmtPlaceTime`, `fmtEventTime` and the rest), because the map and
planner pages call them directly. It does not use `window.BallastWeb`.

Why classic scripts and not ES modules: the consoles and the map page are served as static files, and a page that uses
`<script src>` needs no build step and no change to its markup. Under Node the same files are CommonJS modules, so the tests
run them with `node --test`. Each file has a `module.exports` branch that a browser never reaches.

@ballast/admin needs these files loaded first. It fails with a clear message when `window.BallastWeb` is missing (see its guide).

## html.js: escaping

`window.BallastWeb.escapeHtml(value)` and `window.BallastWeb.safeUrl(value, base)`.

| Function | Gives |
|---|---|
| `escapeHtml(value)` | The text with `&`, `<`, `>`, `"` and `'` written as entities, so it is safe inside element text and in quoted attributes. Null and undefined give an empty string. Any other value is converted to text first. |
| `safeUrl(value, base)` | The address as written when it is an `http:` or `https:` address. A relative address resolves against `base` first. Anything else (`javascript:`, `data:`, a blank value, an address that does not parse) gives `#`. |

`base` is the origin that relative addresses resolve against. In a browser it defaults to the page's own origin. Pass one when
the function runs under Node, or when an address must be judged against another origin.

Failures: neither function throws. Escaping does not stop a `javascript:` link, so a link built from data also goes through
`safeUrl`. Write markup as templates and escape every value from data; never concatenate raw data into markup.

## utc.js: the instant of a server string

`window.BallastWeb.utcInstant(value)` gives a `Date` for a time string the server wrote, or null.

- A string with a time and no zone (`2026-10-01T10:05:00`) is read as UTC: a `Z` is added. The server writes instants in UTC,
  so a string without a zone is one.
- A string with a zone (`...Z` or `...+02:00`) is read as written.
- A date-only string (`2026-10-01`) is read the way ECMAScript reads it, as UTC midnight of that day. It is not given a time.
- Null, undefined, an empty value, or a value that does not parse gives null. A caller must treat null as "no time", never as now.

Example: `utcInstant('2026-10-01T10:05:00')` gives 2026-10-01 10:05 UTC, and its `toISOString()` is `2026-10-01T10:05:00.000Z`.

Failure: the function takes the server's text. It converts its argument to text first, so a `Date` passed in loses its
milliseconds on the way.

## problem.js: reading an error answer

Error answers follow RFC 9457: an object with `title`, `detail` and `status`. Ballast.AspNetCore adds `code` (a stable error
code), `message`, `error`, `errors` (field messages, by field name) and `traceId`.

`readProblemBody(response)` is async. It reads the body once and never throws:

- an empty body gives null;
- a body that parses as JSON gives the parsed value;
- a body that is not JSON gives its trimmed text when it is 300 characters or fewer and does not start with `<`;
- anything else (an HTML error page from a proxy, a long text) gives null, so a page is never shown as a message.

`problemMessage(status, body, options)` gives one line of text for the answer:

1. A string body is returned as it is, or the fallback when it is empty. A body that is neither a string nor an object gives the fallback.
2. When `options.codeText` is a function and the body has a `code` or a `title`, the function is called with `(key, body)`, where
   `key` is the `code` when the body has one, else the `title`. Text it returns is used. Null or undefined means "no text for
   this key", and the next step applies.
3. Otherwise the first non-empty of `detail`, `title`, `message` and `error` is used. With nothing, the fallback is used.
4. Field messages in `errors` are appended after ` — `, as `Field: first; second | Other: third`.
5. The `traceId` is appended as ` (ref X)`, so a support request can name it.

Options: `fallback` (the text when the body says nothing; `Request failed (STATUS)` by default), `preferDetail` (default false;
when true, a 4xx answer with both a `code` and a `detail` shows the detail before `codeText` is asked), and `codeText` (asked
with the `code`, or with the `title` when the body has no `code`).

Failures: `problemMessage` never throws, and a status that is not a number is written as it is. A `codeText` that throws is not
caught, because it is the page's own function; its error reaches the caller.

## i18n.js: the translation loader

`createI18n(options)` returns a loader for a site with several languages. It reads the texts of a language from a JSON file
(`textsPath + lang + '.json'`), keeps a copy in `localStorage` under `i18n_<lang>`, and remembers the visitor's choice in
`localStorage` and in a cookie.

```js
const i18n = window.BallastWeb.createI18n({ langs: ['hr', 'en'], bundled: HR_TEXTS, autoStart: false });
i18n.exposeGlobals(window);
i18n.start();
```

| Option | Default | Meaning |
|---|---|---|
| `langs` | required | The languages. The first is the default. A missing or empty list, or an entry that is not a non-empty text, throws a `TypeError`. |
| `bundled` | null | The texts of `bundledLang`, when the page ships them, so the first paint needs no request. |
| `bundledLang` | the first language | The language `bundled` is written in. |
| `textsPath` | `/i18n/` | Where the JSON files are. |
| `langParam` | `lang` | The query parameter that names a language (`?lang=en`). |
| `storageKey` | `lang` | The `localStorage` key of the visitor's choice. |
| `cachePrefix` | `i18n_` | The prefix of the cached texts. |
| `cookieName` | `lang` | The cookie that carries the choice; null or an empty string for none. |
| `statusMap` | none | Maps an HTTP status to the key of its text. |
| `errorCodeMap` | none | Maps a stable error code to the key of its text. |
| `fallbackKey` | `msg_request_failed` | The key of the text for a failed request. |
| `fallbackText` | `Request failed.` | That text in English, used when the texts do not hold the key. |
| `languageKeyPrefix` | `lang_` | The keys of the language names in the switcher. |
| `switcherId` | `lang-switcher` | The id of the language switcher element. |
| `buttonClass` | `lang-btn` | The class of its buttons. |
| `events` | true | Whether `i18nReady` and `languageChanged` are dispatched on the window. |
| `autoStart` | true | Whether `start()` runs as soon as the loader is created. Set false to call `start()` yourself, after `exposeGlobals`. |
| `env` | the browser's | `window`, `document`, `storage`, `navigator`, `fetch` and `CustomEvent`, for tests. |

The loader returns:

- `getLang()`, the language now in use;
- `setLanguage(lang)`, which returns false for a language the site does not have and otherwise switches and returns true;
- `t(key)`, the text for a key: null while the texts are loading, the key itself when the texts do not hold it;
- `text(key, english)`, the text, or `english` while the texts are loading or the key is missing;
- `status(code)`, the text of an HTTP status from `statusMap`, or the code itself;
- `error(code, fallback)`, the text of an error code from `errorCodeMap`, else `fallback`, else the `fallbackKey` text;
- `problemText(problem, status, fallback, requestLang)`, the text for a failed answer. A 4xx answer with a code and a detail
  shows the detail when the request was sent in the current language. Otherwise it uses `error` with the code, or with the
  `title` when the body has no code (`code || title`); an ordinary title with no mapping gives the fallback;
- `textsReady()`, true once texts are in;
- `start()` and `exposeGlobals(target)`.

`exposeGlobals(target)` (the window by default) installs `__`, `__text`, `getLang`, `__status`, `__error`, `__problemText`,
`setLanguage` and `i18nTextsReady()`. It also sets the global `i18nReady` to true when the texts are in.

Failures:

- A key the texts do not hold is not looked up in the default language. `t` gives the key, and `text` gives the English the page
  passed in.
- The texts are written with `innerHTML` into `data-i18n` elements, so a text may hold markup, such as a link. The texts come
  from the site's own files and bundle, never from a visitor's input, and must be written that way.
- When the file for a language cannot be read and no copy is cached, the loader uses the bundled texts if the page has them. It
  then shows the bundled language without saying so, and `getLang()` still names the chosen one. A page that must say so
  should compare `getLang()` with `bundledLang`.
- With no bundled texts and no file, the page keeps the keys as text until a file arrives.
- Storage that is blocked or full is caught. The choice then lasts for the current page only.

## zone-time.js: times in the place's zone

Two kinds of time are shown differently, and each has its own functions.

- A **place time** is when something happens at a place: a departure, the end of a trip, a service day. It is shown in the
  place's zone. `fmtPlaceTime(iso, tz)` gives the wall clock, such as `10:05`, and adds the zone's offset label (for example
  `GMT+1`) only when the place's offset differs from the device's at that instant, or when the zone is unknown.
  `fmtPlaceTimeLabelled(iso, tz)` always adds the label. `placeParts(iso, tz)` gives `{y, m, d, hh, mm}` in the place's zone.
  `placeNowWall(tz)` gives the wall clock now, in the formats `<input type="date">` and `<input type="time">` take.
  `wallToUtcIso(dateStr, timeStr, tz)` turns a wall clock typed into a form into an instant.
- An **event time** is when something happened to the system: a poll, a check, a last update. It is shown in the viewer's
  zone. `fmtEventTime(iso)`, `fmtEventDateTime(iso)` and `fmtEventDate(iso)` do this.

Helpers the functions above use: `ztToMs`, `ztParts`, `ztOffsetMs`, `ztOffsetLabel` and `ztDeviceZone`. `zoneDiffersFromDevice(tz)`
says whether the zone's offset differs from the device's now, for a label beside an input. `zoneOffsetLabel(tz)` gives the
label, such as `GMT+2`.

The zone argument is an IANA zone id, or null. A **null zone is a missing zone**. It is not guessed: the time is shown in the
device's zone, with the device's offset label, so a guess is never presented as the place's clock. A string with no offset
is then read as the device's local time, which is the browser's own rule. A page that knows a place has no zone should say so
on the screen instead.

A string with no offset and a zone is read as a wall clock in that zone. A string with `Z` or an offset is an instant.

Failures:

- An instant that does not parse gives an empty string from the formatters and null from `placeParts`. It never shows `Invalid Date`.
- An IANA id the browser does not know makes the formatters throw a `RangeError`, from `Intl`. The server writes zone ids that
  the browser knows, so this points at a bad value; it is not hidden.
- `wallToUtcIso` for a wall time in the spring-forward gap gives the instant one hour later on the wall clock. An ambiguous
  time in the autumn overlap gives the earlier instant.

## Vendor copies and the byte-identity check

An app does not load these files from a package manager at run time. It copies them into its own `wwwroot`, so the pages are
served from the app's own origin and the Content-Security-Policy stays `script-src 'self'`. `scripts/vendor-copy.mjs` makes the
copy from a packed `.tgz`:

```
node scripts/vendor-copy.mjs copy  --package artifacts/packages/ballast-web-0.1.0-preview.1.tgz --app <wwwroot>
node scripts/vendor-copy.mjs check --package artifacts/packages/ballast-web-0.1.0-preview.1.tgz --app <wwwroot>
```

- `--dest` is relative to `--app`. It defaults to `vendor/ballast/<name>`, where `<name>` is the package name without its
  scope, so `@ballast/web` goes to `vendor/ballast/web`.
- `--map <file>=<target>` sends one browser file somewhere else, relative to `--app`. It can be given more than once.
- `copy` writes the files and a manifest, `ballast-vendor.json`, with each file's source name, target and SHA-256. It removes
  copies that the new package no longer has.
- `check` reads the package and compares every target with its hash. It writes nothing.

Exit codes: `0` when the copy is written or the check passes, `1` when a copy differs or is missing, `2` for a usage error or a
package that cannot be read. A target outside `--app` is refused.

An app that keeps its vendored files in Git on Windows must mark them as binary in `.gitattributes`
(`wwwroot/vendor/ballast/** -text`). Without it, Git converts line endings and the byte-identity check fails on a clean
checkout.

## Package contents

`browser/` holds the five files above. `test/` holds the `node:test` suite. It loads each file as CommonJS, and also in a `vm`
context with `window` set, as a page would. Run it with `npm test` in `js/web`.
