// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The admin console's sign-in page: a password form, a Microsoft button, the return from Microsoft, and the way back to the page
// the operator came from.
//
// Microsoft's sign-in comes back to this page as ?code= (a one-time code to exchange for a session) or ?error= (a refusal to show).
// The query is cleared at once, so a reload never repeats either. Only the refusal codes in the message table reach the page;
// anything else gets the generic line. The back-link is honoured only when it is a same-origin page inside the console (the
// admin prefix, and not the sign-in page itself), so a stale or tampered value cannot send the operator off the site.
//
// In a browser this file is a classic <script>: it installs createLoginPage on window.BallastAdmin. Under CommonJS (Node tests) it
// exports it instead.
(function () {
  'use strict';

  var root = typeof window !== 'undefined' ? window : globalThis;

  // The text for each refusal. These are the codes the server sends back; the defaults match them, and a console may replace any.
  var MICROSOFT_MESSAGES = {
    MS_NOT_CONFIGURED: 'Microsoft sign-in is not set up yet.',
    MS_SIGNIN_FAILED: 'Microsoft sign-in did not finish.',
    MS_WRONG_TENANT: 'This Microsoft account is not one of ours.',
    MS_NAME_MISSING: 'Your first or last name is missing in Microsoft.',
    MS_NO_ROLE: 'You have no role for this console in Microsoft.',
    MS_ROLE_NO_PERMISSIONS: 'Your roles have no permissions here yet.',
    MS_ADDRESS_TAKEN: 'Another account uses this address here.',
    SIGNIN_CODE_INVALID: 'The sign-in expired. Try again.'
  };

  var DEFAULT_TEXT = {
    signingIn: 'Signing in…',
    signIn: 'Sign in',
    invalid: 'Invalid credentials.',
    failed: 'Sign-in failed',
    network: 'Network error — is the API running?',
    expired: 'The sign-in expired. Try again.',
    unknownMicrosoft: 'Microsoft sign-in did not finish.',
    microsoftCode: "Microsoft's code",
    showPassword: 'Show password',
    hidePassword: 'Hide password'
  };

  // Microsoft's own code is shown only in this shape: an AADSTS number, or an OAuth error word.
  var MICROSOFT_PROVIDER_CODE = /^(AADSTS\d{4,7}|[a-z_]{1,40})$/;

  /**
   * The page to go to after sign-in, from the address the operator came from (kept in sessionStorage under redirectKey). The key is
   * removed in every case. Only a same-origin address inside the console is honoured, and never the sign-in page itself.
   * @param {{getItem: function, removeItem: function}} storage
   * @param {object} options {redirectKey, origin, adminPrefix, loginPageSuffix, homePath}
   * @returns {string} a path, with its query and hash
   */
  function redirectTargetFor(storage, options) {
    var stored = storage.getItem(options.redirectKey);
    storage.removeItem(options.redirectKey);
    if (!stored) return options.homePath;
    try {
      var url = new URL(stored, options.origin);
      if (url.origin === options.origin && url.pathname.indexOf(options.adminPrefix) === 0 &&
          !url.pathname.endsWith(options.loginPageSuffix)) {
        return url.pathname + url.search + url.hash;
      }
    } catch (e) {
      // Not a parseable address: the home page is used instead.
    }
    return options.homePath;
  }

  /**
   * The text for a Microsoft refusal. Microsoft's own code is appended only when it has the expected shape.
   * @param {string} code the refusal code from the address
   * @param {string|null} providerCode Microsoft's code from the address, or null
   * @param {object} [messages] refusal texts that replace the defaults
   * @param {object} [text] the wording of the page
   * @returns {string}
   */
  function microsoftErrorText(code, providerCode, messages, text) {
    var table = Object.assign({}, MICROSOFT_MESSAGES, messages || {});
    var wording = Object.assign({}, DEFAULT_TEXT, text || {});
    var line = Object.prototype.hasOwnProperty.call(table, code) ? table[code] : wording.unknownMicrosoft;
    return providerCode && MICROSOFT_PROVIDER_CODE.test(providerCode)
      ? line + ' (' + wording.microsoftCode + ': ' + providerCode + ')'
      : line;
  }

  /**
   * Creates the sign-in page's behaviour and wires it to the page's elements.
   * @param {object} options
   * @param {object} options.auth the sign-in state from createAuth. Required.
   * @param {object} [options.ids] element ids: form, identifier, password, reveal, submit, error, microsoft.
   * @param {string} [options.identifierField] the request field for the e-mail address, 'email' by default.
   * @param {string} [options.loginPath] the password sign-in endpoint, '/auth/login' by default.
   * @param {string} [options.microsoftStartPath] '/auth/microsoft/start' by default.
   * @param {string} [options.microsoftExchangePath] '/auth/microsoft/exchange' by default.
   * @param {string} [options.homePath] where to go after sign-in when there is no back-link, '/admin/' by default.
   * @param {string} [options.adminPrefix] the only path prefix a back-link may name, '/admin/' by default.
   * @param {string} [options.loginPageSuffix] the sign-in page's file name, '/login.html' by default.
   * @param {object} [options.messages] Microsoft refusal texts that replace the defaults.
   * @param {object} [options.text] the wording of the page, which replaces the defaults.
   * @param {object} [options.document] document, by default.
   * @param {object} [options.location] window.location, by default.
   * @param {object} [options.history] window.history, by default.
   * @param {function} [options.fetch] fetch, by default the page's own.
   * @returns {{start: function(): void}} start() wires the page; call it once.
   */
  function createLoginPage(options) {
    var opt = options || {};
    if (!opt.auth) throw new TypeError('createLoginPage needs the sign-in state: pass auth from createAuth.');
    var auth = opt.auth;
    var ids = Object.assign({
      form: 'loginForm', identifier: 'email', password: 'password', reveal: 'reveal',
      submit: 'submit', error: 'error', microsoft: 'microsoft'
    }, opt.ids || {});
    var identifierField = opt.identifierField || 'email';
    var loginPath = opt.loginPath || '/auth/login';
    var microsoftStartPath = opt.microsoftStartPath || '/auth/microsoft/start';
    var microsoftExchangePath = opt.microsoftExchangePath || '/auth/microsoft/exchange';
    var homePath = opt.homePath || '/admin/';
    var adminPrefix = opt.adminPrefix || '/admin/';
    var loginPageSuffix = opt.loginPageSuffix || '/login.html';
    var messages = opt.messages || {};
    var text = Object.assign({}, DEFAULT_TEXT, opt.text || {});

    function doc() { return opt.document || root.document; }
    function loc() { return opt.location || root.location; }
    function hist() { return opt.history || root.history; }
    function doFetch(path, init) { return (opt.fetch || root.fetch)(path, init); }
    function el(key) { return doc().getElementById(ids[key]); }

    function origin() { return loc().origin; }

    function takeRedirect() {
      return redirectTargetFor(auth.storage(), {
        redirectKey: auth.keys.redirect, origin: origin(), adminPrefix: adminPrefix,
        loginPageSuffix: loginPageSuffix, homePath: homePath
      });
    }

    function showError(message) {
      var box = el('error');
      box.textContent = message;
      box.classList.remove('hidden');
    }

    function hideError() {
      el('error').classList.add('hidden');
    }

    function setBusy(busy, label) {
      var button = el('submit');
      button.disabled = busy;
      button.textContent = label;
    }

    function fail(message) {
      showError(message);
      setBusy(false, text.signIn);
    }

    function fetchFailure(status) {
      return status === 401 || status === 400 ? text.invalid : text.failed + ' (' + status + ').';
    }

    // Stores the tokens and leaves for the page the operator came from.
    function completeSignIn(data) {
      auth.setTokens(data);
      loc().href = takeRedirect();
    }

    async function signInWithPassword() {
      hideError();
      setBusy(true, text.signingIn);
      var body = {};
      body[identifierField] = el('identifier').value;
      body.password = el('password').value;
      try {
        var res = await doFetch(loginPath, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body)
        });
        if (!res.ok) { fail(fetchFailure(res.status)); return; }
        var data = await res.json();
        if (!data || !data.accessToken) { fail(text.failed + '.'); return; }
        completeSignIn(data);
      } catch (ex) {
        fail(text.network);
      }
    }

    async function exchangeMicrosoftCode(code) {
      hideError();
      setBusy(true, text.signingIn);
      try {
        var res = await doFetch(microsoftExchangePath, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code: code })
        });
        if (!res.ok) { fail(text.expired); return; }
        var data = await res.json();
        if (!data || !data.accessToken) { fail(text.expired); return; }
        completeSignIn(data);
      } catch (ex) {
        fail(text.network);
      }
    }

    /**
     * Wires the page and handles a return from Microsoft. Call it once, when the page has loaded.
     */
    function start() {
      var returned = new URLSearchParams(loc().search);
      var code = returned.get('code');
      var error = returned.get('error');
      var providerCode = returned.get('ms');
      hist().replaceState(null, '', loc().pathname);

      el('reveal').addEventListener('click', function () {
        var pw = el('password');
        var show = pw.type === 'password';
        pw.type = show ? 'text' : 'password';
        var button = el('reveal');
        button.title = show ? text.hidePassword : text.showPassword;
        button.firstElementChild.className = 'icon ' + (show ? 'icon-eye-off' : 'icon-eye');
        button.firstElementChild.style.width = '18px';
        button.firstElementChild.style.height = '18px';
      });

      el('form').addEventListener('submit', function (event) {
        event.preventDefault();
        signInWithPassword();
      });

      // A full-page navigation, never a form post: the console's policy allows form-action 'self' only. The address typed in the
      // e-mail field goes along as the sign-in hint, so Microsoft's page can pre-fill the same account.
      el('microsoft').addEventListener('click', function () {
        var hint = el('identifier').value.trim().toLowerCase();
        loc().href = microsoftStartPath + (hint ? '?login_hint=' + encodeURIComponent(hint) : '');
      });

      if (code) {
        exchangeMicrosoftCode(code);
      } else if (error) {
        showError(microsoftErrorText(error, providerCode, messages, text));
      }
    }

    return { start: start };
  }

  var exported = {
    createLoginPage: createLoginPage,
    redirectTargetFor: redirectTargetFor,
    microsoftErrorText: microsoftErrorText
  };

  if (typeof module === 'object' && module && module.exports) {
    module.exports = exported;
  } else {
    root.BallastAdmin = Object.assign(root.BallastAdmin || {}, exported);
  }
})();
