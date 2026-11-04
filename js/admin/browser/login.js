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
   * @param {object|function} [options.messages] Microsoft refusal texts that replace the defaults. A function returns them, and is
   *   called each time the page draws one.
   * @param {object|function} [options.text] the wording of the page, which replaces the defaults. A function returns it, and is
   *   called each time the page draws text, so the wording follows the language.
   * @param {string|null} [options.languageEvent] 'languageChanged' by default: the event that draws the page's text again. The
   *   @ballast/web loader dispatches it on the window when the language changes (detail.lang holds the new language). null turns
   *   the redraw off.
   * @param {object} [options.eventTarget] what languageEvent is listened for: the window, by default.
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
    var languageEvent = opt.languageEvent === undefined ? 'languageChanged' : opt.languageEvent;

    // The wording and the refusal texts are read each time they are drawn, so a function (for a language that has changed) is asked again.
    function wording() {
      var source = typeof opt.text === 'function' ? opt.text() : opt.text;
      return Object.assign({}, DEFAULT_TEXT, source || {});
    }

    function refusalTexts() {
      var source = typeof opt.messages === 'function' ? opt.messages() : opt.messages;
      return source || {};
    }

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

    // The error line is kept as a function that draws it from the wording, so a language switch redraws it as well.
    var shownError = null;

    function drawError() {
      var box = el('error');
      if (!shownError) {
        box.classList.add('hidden');
        return;
      }
      box.textContent = shownError();
      box.classList.remove('hidden');
    }

    function showError(drawLine) {
      shownError = drawLine;
      drawError();
    }

    function hideError() {
      shownError = null;
      el('error').classList.add('hidden');
    }

    // The submit button is disabled and labelled while a request is in flight.
    var busy = false;

    function drawSubmit() {
      var button = el('submit');
      var t = wording();
      button.disabled = busy;
      button.textContent = busy ? t.signingIn : t.signIn;
    }

    function setBusy(on) {
      busy = on;
      drawSubmit();
    }

    // The text of one key of the wording, read at the moment the line is drawn.
    function line(key) {
      return function () { return wording()[key]; };
    }

    function fail(drawLine) {
      showError(drawLine);
      setBusy(false);
    }

    function fetchFailure(status) {
      var t = wording();
      return status === 401 || status === 400 ? t.invalid : t.failed + ' (' + status + ').';
    }

    // The show-password button: aria-pressed says whether the password is shown, aria-label and title name what a click does, and
    // the icon (the button's first element child, when it has one) matches. The page supplies the button, and should start it with
    // aria-pressed="false".
    function drawReveal() {
      var button = el('reveal');
      var shown = el('password').type === 'text';
      var t = wording();
      var label = shown ? t.hidePassword : t.showPassword;
      button.setAttribute('aria-pressed', shown ? 'true' : 'false');
      button.setAttribute('aria-label', label);
      button.title = label;
      var icon = button.firstElementChild;
      if (icon) {
        icon.className = 'icon ' + (shown ? 'icon-eye-off' : 'icon-eye');
        icon.style.width = '18px';
        icon.style.height = '18px';
      }
    }

    // A language switch draws the page's own text again: the submit button, the show-password button and the error line.
    function redraw() {
      drawSubmit();
      drawReveal();
      drawError();
    }

    // Stores the tokens and leaves for the page the operator came from.
    function completeSignIn(data) {
      auth.setTokens(data);
      loc().href = takeRedirect();
    }

    async function signInWithPassword() {
      hideError();
      setBusy(true);
      var body = {};
      body[identifierField] = el('identifier').value;
      body.password = el('password').value;
      try {
        var res = await doFetch(loginPath, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body)
        });
        if (!res.ok) {
          var status = res.status;
          fail(function () { return fetchFailure(status); });
          return;
        }
        var data = await res.json();
        if (!data || !data.accessToken) { fail(function () { return wording().failed + '.'; }); return; }
        completeSignIn(data);
      } catch (ex) {
        fail(line('network'));
      }
    }

    async function exchangeMicrosoftCode(code) {
      hideError();
      setBusy(true);
      try {
        var res = await doFetch(microsoftExchangePath, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code: code })
        });
        if (!res.ok) { fail(line('expired')); return; }
        var data = await res.json();
        if (!data || !data.accessToken) { fail(line('expired')); return; }
        completeSignIn(data);
      } catch (ex) {
        fail(line('network'));
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
        pw.type = pw.type === 'password' ? 'text' : 'password';
        drawReveal();
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

      var target = opt.eventTarget || root;
      if (languageEvent && target && typeof target.addEventListener === 'function') {
        target.addEventListener(languageEvent, redraw);
      }

      if (code) {
        exchangeMicrosoftCode(code);
      } else if (error) {
        showError(function () { return microsoftErrorText(error, providerCode, refusalTexts(), wording()); });
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
