// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The admin console's sign-in state: the tokens in sessionStorage, one shared refresh, and the bearer token on the console's own
// requests.
//
// The access token expires after 15 minutes. The refresh token is rotated: the server claims it once, and a second use is read
// as theft and revokes the whole session. So there is exactly one refresh in flight at a time (every caller waits for it), and it
// is bounded by a timeout, so one stalled request cannot hold every later 401 until a reload.
//
// createAuth(options) returns the state. install() also wraps window.fetch, so a page's own fetch calls to the console's API carry
// the token and renew it once when it expires. Tokens never go to another origin.
//
// In a browser this file is a classic <script>: it installs createAuth on window.BallastAdmin. Under CommonJS (Node tests) it
// exports it instead.
(function () {
  'use strict';

  var root = typeof window !== 'undefined' ? window : globalThis;

  var DEFAULTS = {
    tokenKey: 'auth_token',
    refreshKey: 'refresh_token',
    redirectKey: 'redirect_after_login',
    loginPath: '/admin/login.html',
    loginPageSuffix: '/login.html',
    refreshPath: '/auth/refresh',
    refreshTimeoutMs: 15000
  };

  // Decodes the payload of a JWT (the middle part) as UTF-8 JSON. Null for anything that is not one.
  function decodeClaims(token) {
    if (!token) return null;
    try {
      var part = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      var binary = atob(part);
      var bytes = Uint8Array.from(binary, function (c) { return c.charCodeAt(0); });
      return JSON.parse(new TextDecoder('utf-8').decode(bytes));
    } catch (e) {
      return null;
    }
  }

  /**
   * Creates the sign-in state for one console.
   * @param {object} [options]
   * @param {object} [options.storage] sessionStorage, by default (getItem, setItem, removeItem)
   * @param {object} [options.location] window.location, by default
   * @param {function} [options.fetch] the fetch the refresh uses, by default window.fetch (bound)
   * @param {string} [options.tokenKey] 'auth_token' by default
   * @param {string} [options.refreshKey] 'refresh_token' by default
   * @param {string} [options.redirectKey] 'redirect_after_login' by default: where to go back to after sign-in
   * @param {string} [options.loginPath] the sign-in page, '/admin/login.html' by default
   * @param {string} [options.loginPageSuffix] a page whose path ends with this is never redirected to sign-in
   * @param {string} [options.refreshPath] '/auth/refresh' by default; it takes { refreshToken } and answers { accessToken, refreshToken }
   * @param {number} [options.refreshTimeoutMs] how long a refresh may take before it counts as failed; 15000 by default
   * @returns {object}
   */
  function createAuth(options) {
    var opt = options || {};
    var keys = {
      token: opt.tokenKey || DEFAULTS.tokenKey,
      refresh: opt.refreshKey || DEFAULTS.refreshKey,
      redirect: opt.redirectKey || DEFAULTS.redirectKey
    };
    var loginPath = opt.loginPath || DEFAULTS.loginPath;
    var loginPageSuffix = opt.loginPageSuffix || DEFAULTS.loginPageSuffix;
    var refreshPath = opt.refreshPath || DEFAULTS.refreshPath;
    var refreshTimeoutMs = opt.refreshTimeoutMs || DEFAULTS.refreshTimeoutMs;

    function storage() { return opt.storage || root.sessionStorage; }
    function location() { return opt.location || root.location; }
    function baseFetch() { return opt.fetch || root.fetch.bind(root); }

    function token() {
      return storage().getItem(keys.token) || '';
    }

    function setTokens(tokens) {
      if (!tokens || !tokens.accessToken) throw new TypeError('setTokens needs an accessToken.');
      storage().setItem(keys.token, tokens.accessToken);
      if (tokens.refreshToken) storage().setItem(keys.refresh, tokens.refreshToken);
    }

    function clearTokens() {
      storage().removeItem(keys.token);
      storage().removeItem(keys.refresh);
    }

    function headers() {
      var h = { 'Content-Type': 'application/json' };
      var t = token();
      if (t) h.Authorization = 'Bearer ' + t;
      return h;
    }

    function claims() {
      return decodeClaims(token());
    }

    function toLogin() {
      storage().setItem(keys.redirect, location().href);
      location().href = loginPath;
    }

    /** Signs out: forgets both tokens and goes to the sign-in page. Nothing is kept for a later visit. */
    function signOut() {
      clearTokens();
      location().href = loginPath;
    }

    /**
     * True when there is a token. Without one, the current page is remembered and the sign-in page is opened, and this returns false.
     * @returns {boolean}
     */
    function requireSignedIn() {
      if (token()) return true;
      toLogin();
      return false;
    }

    var refreshInFlight = null;

    /**
     * Exchanges the refresh token for a new pair. Concurrent callers share one request. Resolves to false when there is no refresh
     * token, the server refuses it, the answer is not a token pair, or the request takes longer than refreshTimeoutMs.
     * @returns {Promise<boolean>}
     */
    function refresh() {
      if (refreshInFlight) return refreshInFlight;

      var stored = storage().getItem(keys.refresh);
      if (!stored) return Promise.resolve(false);

      var timeout = new Promise(function (_, reject) {
        setTimeout(function () { reject(new Error('refresh timeout')); }, refreshTimeoutMs);
      });
      refreshInFlight = Promise.race([
        baseFetch()(refreshPath, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken: stored })
        }),
        timeout
      ]).then(function (res) {
        if (!res.ok) return false;
        return res.json().then(function (data) {
          if (!data || !data.accessToken) return false;
          setTokens(data);
          return true;
        });
      }).catch(function () {
        return false;
      }).finally(function () {
        refreshInFlight = null;
      });

      return refreshInFlight;
    }

    // True for requests to the console's own origin, the only ones the token belongs on. A Request object is read by its url:
    // as a string it would resolve as a relative path, which would classify every Request as same-origin.
    function isSameOrigin(input) {
      try {
        var href = (input && typeof input === 'object' && typeof input.url === 'string') ? input.url : input;
        return new URL(href, location().origin).origin === location().origin;
      } catch (e) {
        return false;
      }
    }

    // A copy with the token set, so the caller's options object is never written to (one request's credentials must not leak into the next).
    function withAuth(opts) {
      var next = Object.assign({}, opts || {});
      var h = new Headers(next.headers || {});
      var t = token();
      if (t) h.set('Authorization', 'Bearer ' + t);
      next.headers = h;
      return next;
    }

    function fail() {
      clearTokens();
      toLogin();
    }

    /**
     * Wraps window.fetch for the console's own requests: the token is attached, a 429 is retried once after its Retry-After (at most
     * 5 seconds), and a 401 renews the token once and retries. A renewal that fails sends the operator to sign in again.
     * When redirectWhenSignedOut is set and there is no token, the sign-in page is opened at once (never on the sign-in page itself).
     * @param {{redirectWhenSignedOut?: boolean}} [installOptions]
     * @returns {object} this state
     */
    function install(installOptions) {
      var origFetch = baseFetch();

      root.fetch = function (input, init) {
        if (!isSameOrigin(input)) return origFetch(input, init);

        return origFetch(input, withAuth(init)).then(function (res) {
          if (res.status === 429) {
            var ra = parseInt(res.headers.get('Retry-After') || '2', 10);
            var delay = isNaN(ra) ? 2000 : Math.min(ra * 1000, 5000);
            return new Promise(function (resolve) { setTimeout(resolve, delay); })
              .then(function () { return origFetch(input, withAuth(init)); })
              .then(function (again) {
                if (again.status !== 401) return again;
                return refresh().then(function (renewed) {
                  if (!renewed) { fail(); return again; }
                  return origFetch(input, withAuth(init));
                });
              });
          }
          if (res.status !== 401) return res;

          return refresh().then(function (renewed) {
            if (!renewed) { fail(); return res; }
            return origFetch(input, withAuth(init));
          });
        });
      };

      var path = location().pathname || '';
      if (installOptions && installOptions.redirectWhenSignedOut && !token() && path.slice(-loginPageSuffix.length) !== loginPageSuffix) {
        toLogin();
      }
      return state;
    }

    var state = {
      token: token,
      setTokens: setTokens,
      clearTokens: clearTokens,
      headers: headers,
      claims: claims,
      toLogin: toLogin,
      signOut: signOut,
      requireSignedIn: requireSignedIn,
      refresh: refresh,
      install: install,
      storage: storage,
      loginPath: loginPath,
      keys: keys
    };
    return state;
  }

  var exported = { createAuth: createAuth, decodeClaims: decodeClaims };

  if (typeof module === 'object' && module && module.exports) {
    module.exports = exported;
  } else {
    root.BallastAdmin = Object.assign(root.BallastAdmin || {}, exported);
  }
})();
