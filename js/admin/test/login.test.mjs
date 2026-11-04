// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

import { fakeDocument, fakeElement, fakeLocation, fakeStorage } from './fake-dom.mjs';

const require = createRequire(import.meta.url);
const { createAuth } = require('../browser/auth.js');
const { createLoginPage, microsoftErrorText, redirectTargetFor } = require('../browser/login.js');

const ORIGIN = 'https://console.example.test';
const REDIRECT = {
  redirectKey: 'redirect_after_login', origin: ORIGIN, adminPrefix: '/admin/', loginPageSuffix: '/login.html', homePath: '/admin/'
};

function json(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function storedAs(value) {
  const storage = fakeStorage(value === null ? {} : { redirect_after_login: value });
  return storage;
}

describe('redirectTargetFor', () => {
  it('goes back to a page inside the console, with its query and hash', () => {
    const storage = storedAs(`${ORIGIN}/admin/feeds.html?page=2#feed-12`);
    assert.equal(redirectTargetFor(storage, REDIRECT), '/admin/feeds.html?page=2#feed-12');
  });

  it('removes the stored address in every case, so it is used once', () => {
    const storage = storedAs(`${ORIGIN}/admin/feeds.html`);
    redirectTargetFor(storage, REDIRECT);
    assert.equal(storage.getItem('redirect_after_login'), null);
  });

  it('goes to the home page when nothing is stored', () => {
    assert.equal(redirectTargetFor(storedAs(null), REDIRECT), '/admin/');
  });

  it('refuses an address on another origin', () => {
    assert.equal(redirectTargetFor(storedAs('https://evil.example.test/admin/feeds.html'), REDIRECT), '/admin/');
  });

  it('refuses a path outside the console prefix, even on this origin', () => {
    assert.equal(redirectTargetFor(storedAs(`${ORIGIN}/api/users`), REDIRECT), '/admin/');
    assert.equal(redirectTargetFor(storedAs(`${ORIGIN}/administrator/x.html`), REDIRECT), '/admin/');
  });

  it('never goes back to the sign-in page itself', () => {
    assert.equal(redirectTargetFor(storedAs(`${ORIGIN}/admin/login.html`), REDIRECT), '/admin/');
  });

  it('refuses a javascript: address', () => {
    assert.equal(redirectTargetFor(storedAs('javascript:alert(1)'), REDIRECT), '/admin/');
  });
});

describe('microsoftErrorText', () => {
  it('gives the text of a known refusal', () => {
    assert.equal(microsoftErrorText('MS_NO_ROLE', null), 'You have no role for this console in Microsoft.');
  });

  it('gives the generic line for a refusal it does not know', () => {
    assert.equal(microsoftErrorText('SOMETHING_ELSE', null), 'Microsoft sign-in did not finish.');
    assert.equal(microsoftErrorText('constructor', null), 'Microsoft sign-in did not finish.');
  });

  it('shows Microsoft\'s code only in the expected shape', () => {
    assert.equal(microsoftErrorText('MS_SIGNIN_FAILED', 'AADSTS50011'),
      'Microsoft sign-in did not finish. (Microsoft\'s code: AADSTS50011)');
    assert.equal(microsoftErrorText('MS_SIGNIN_FAILED', 'access_denied'),
      'Microsoft sign-in did not finish. (Microsoft\'s code: access_denied)');
    assert.equal(microsoftErrorText('MS_SIGNIN_FAILED', '<script>'), 'Microsoft sign-in did not finish.');
  });

  it('lets the console replace a text, and keeps the others', () => {
    assert.equal(microsoftErrorText('MS_WRONG_TENANT', null, { MS_WRONG_TENANT: 'Not ours.' }), 'Not ours.');
    assert.equal(microsoftErrorText('MS_NO_ROLE', null, { MS_WRONG_TENANT: 'Not ours.' }), 'You have no role for this console in Microsoft.');
  });
});

function loginDom({ icon = true } = {}) {
  const ids = {
    loginForm: fakeElement('loginForm'),
    email: fakeElement('email', { value: 'ana@example.test' }),
    password: fakeElement('password', { value: 'secret', type: 'password' }),
    reveal: fakeElement('reveal', { firstElementChild: icon ? fakeElement('icon') : null }),
    submit: fakeElement('submit', { value: '' }),
    error: fakeElement('error', { classes: ['hidden'] }),
    microsoft: fakeElement('microsoft')
  };
  const document = fakeDocument(ids);
  return { ids, document };
}

function signInPage({ href = `${ORIGIN}/admin/login.html`, search = '', fetchImpl, storage, icon, text, messages, languageEvent, eventTarget } = {}) {
  const dom = loginDom({ icon });
  const location = fakeLocation(`${href}${search}`);
  const history = { replaced: [], replaceState(_s, _t, url) { this.replaced.push(url); } };
  const state = storage || fakeStorage();
  const calls = [];
  const fetch = async (input, init) => {
    calls.push({ input, init });
    return fetchImpl ? fetchImpl(input, init) : json({ accessToken: 'a1', refreshToken: 'r1' });
  };
  const auth = createAuth({ storage: state, location, fetch });
  const page = createLoginPage({ auth, document: dom.document, location, history, fetch, text, messages, languageEvent, eventTarget });
  return { ...dom, page, location, history, storage: state, calls, auth };
}

async function submitForm(page) {
  // The submit handler is async: the form's listener starts it, and the test waits for the request to finish.
  page.ids.loginForm.dispatch('submit', { preventDefault() {} });
  await new Promise((resolve) => setTimeout(resolve, 5));
}

describe('createLoginPage: signing in with a password', () => {
  it('sends the e-mail address and password, stores the tokens and goes back to the page it came from', async () => {
    const storage = fakeStorage({ redirect_after_login: `${ORIGIN}/admin/feeds.html` });
    const p = signInPage({ storage });
    p.page.start();

    await submitForm(p);

    assert.equal(p.calls[0].input, '/auth/login');
    assert.deepEqual(JSON.parse(p.calls[0].init.body), { email: 'ana@example.test', password: 'secret' });
    assert.equal(storage.getItem('auth_token'), 'a1');
    assert.equal(storage.getItem('refresh_token'), 'r1');
    assert.deepEqual(p.location.navigations, ['/admin/feeds.html']);
  });

  it('shows a refused sign-in as invalid credentials, and enables the button again', async () => {
    const p = signInPage({ fetchImpl: () => json({ title: 'Nope' }, 401) });
    p.page.start();

    await submitForm(p);

    assert.equal(p.ids.error.textContent, 'Invalid credentials.');
    assert.equal(p.ids.error.classList.contains('hidden'), false);
    assert.equal(p.ids.submit.disabled, false);
    assert.equal(p.ids.submit.textContent, 'Sign in');
    assert.deepEqual(p.location.navigations, []);
  });

  it('shows the status for any other refusal', async () => {
    const p = signInPage({ fetchImpl: () => json({}, 500) });
    p.page.start();

    await submitForm(p);

    assert.equal(p.ids.error.textContent, 'Sign-in failed (500).');
  });

  it('shows a network error when the request fails', async () => {
    const p = signInPage({ fetchImpl: () => { throw new TypeError('network down'); } });
    p.page.start();

    await submitForm(p);

    assert.equal(p.ids.error.textContent, 'Network error — is the API running?');
  });

  it('treats an answer with no access token as a failed sign-in, and stores nothing', async () => {
    const p = signInPage({ fetchImpl: () => json({ refreshToken: 'r1' }) });
    p.page.start();

    await submitForm(p);

    assert.equal(p.storage.getItem('auth_token'), null);
    assert.equal(p.ids.error.textContent, 'Sign-in failed.');
  });
});

describe('createLoginPage: Microsoft', () => {
  it('starts the Microsoft sign-in with the address typed as the hint', () => {
    const p = signInPage();
    p.page.start();
    p.ids.email.value = '  Ana@Example.test ';

    p.ids.microsoft.dispatch('click');

    assert.deepEqual(p.location.navigations, ['/auth/microsoft/start?login_hint=ana%40example.test']);
  });

  it('starts the Microsoft sign-in with no hint when the address is empty', () => {
    const p = signInPage();
    p.page.start();
    p.ids.email.value = '';

    p.ids.microsoft.dispatch('click');

    assert.deepEqual(p.location.navigations, ['/auth/microsoft/start']);
  });

  it('exchanges the one-time code that comes back, and clears the address at once', async () => {
    const storage = fakeStorage({ redirect_after_login: `${ORIGIN}/admin/` });
    const p = signInPage({ search: '?code=abc123', storage });
    p.page.start();

    assert.deepEqual(p.history.replaced, ['/admin/login.html']);
    await new Promise((resolve) => setTimeout(resolve, 5));

    assert.equal(p.calls[0].input, '/auth/microsoft/exchange');
    assert.deepEqual(JSON.parse(p.calls[0].init.body), { code: 'abc123' });
    assert.equal(storage.getItem('auth_token'), 'a1');
    assert.deepEqual(p.location.navigations, ['/admin/']);
  });

  it('shows the expired-sign-in text when the exchange is refused', async () => {
    const p = signInPage({ search: '?code=old', fetchImpl: () => json({}, 400) });
    p.page.start();
    await new Promise((resolve) => setTimeout(resolve, 5));

    assert.equal(p.ids.error.textContent, 'The sign-in expired. Try again.');
  });

  it('shows the refusal that comes back in the address, with Microsoft\'s code when it has the expected shape', () => {
    const p = signInPage({ search: '?error=MS_NO_ROLE&ms=AADSTS50105' });
    p.page.start();

    assert.equal(p.ids.error.textContent,
      'You have no role for this console in Microsoft. (Microsoft\'s code: AADSTS50105)');
    assert.equal(p.calls.length, 0);
  });

  it('reveals and hides the password', () => {
    const p = signInPage();
    p.page.start();

    p.ids.reveal.dispatch('click');
    assert.equal(p.ids.password.type, 'text');
    assert.equal(p.ids.reveal.title, 'Hide password');

    p.ids.reveal.dispatch('click');
    assert.equal(p.ids.password.type, 'password');
    assert.equal(p.ids.reveal.title, 'Show password');
  });
});

// A window-like target that records the listeners, and fires an event for a test.
function fakeEventTarget() {
  const listeners = {};
  return {
    listeners,
    addEventListener(type, fn) { (listeners[type] ||= []).push(fn); },
    fire(type, detail) { (listeners[type] || []).forEach((fn) => fn({ type, detail })); }
  };
}

describe('createLoginPage: the show-password button', () => {
  it('toggles aria-pressed and the label on a button with no icon child, and sets nothing on an icon that is not there', () => {
    const p = signInPage({ icon: false });
    p.page.start();

    p.ids.reveal.dispatch('click');
    assert.equal(p.ids.password.type, 'text');
    assert.equal(p.ids.reveal.getAttribute('aria-pressed'), 'true');
    assert.equal(p.ids.reveal.getAttribute('aria-label'), 'Hide password');
    assert.equal(p.ids.reveal.title, 'Hide password');

    p.ids.reveal.dispatch('click');
    assert.equal(p.ids.password.type, 'password');
    assert.equal(p.ids.reveal.getAttribute('aria-pressed'), 'false');
    assert.equal(p.ids.reveal.getAttribute('aria-label'), 'Show password');
  });

  it('also sets the icon class and size when the button has an icon child', () => {
    const p = signInPage({ icon: true });
    p.page.start();

    p.ids.reveal.dispatch('click');
    assert.equal(p.ids.reveal.firstElementChild.className, 'icon icon-eye-off');
    assert.equal(p.ids.reveal.firstElementChild.style.width, '18px');

    p.ids.reveal.dispatch('click');
    assert.equal(p.ids.reveal.firstElementChild.className, 'icon icon-eye');
  });
});

describe('createLoginPage: the wording and the language switch', () => {
  const WORDS = {
    en: { invalid: 'Invalid credentials.', signIn: 'Sign in', signingIn: 'Signing in…', showPassword: 'Show password', hidePassword: 'Hide password' },
    hr: { invalid: 'Neispravni podaci.', signIn: 'Prijava', signingIn: 'Prijavljujem…', showPassword: 'Prikaži lozinku', hidePassword: 'Sakrij lozinku' }
  };

  it('takes the wording as a function, which is asked each time the page draws text', async () => {
    let asked = 0;
    const p = signInPage({ fetchImpl: () => json({}, 401), text: () => { asked += 1; return WORDS.en; } });
    p.page.start();
    const before = asked;

    await submitForm(p);

    assert.equal(p.ids.error.textContent, 'Invalid credentials.');
    assert.equal(p.ids.submit.textContent, 'Sign in');
    assert.ok(asked > before, 'the wording function is asked again when the page draws');
  });

  it('keeps the object form of the wording, with the defaults for anything it leaves out', () => {
    const p = signInPage({ text: { signIn: 'Log in' } });
    p.page.start();

    p.ids.reveal.dispatch('click');
    assert.equal(p.ids.reveal.title, 'Hide password');
  });

  it('takes the Microsoft refusal texts as an object, or as a function asked when the refusal is drawn', () => {
    const asObject = signInPage({ search: '?error=MS_NO_ROLE', messages: { MS_NO_ROLE: 'Not yours.' } });
    asObject.page.start();
    assert.equal(asObject.ids.error.textContent, 'Not yours.');

    let asked = 0;
    const asFunction = signInPage({ search: '?error=MS_NO_ROLE', messages: () => { asked += 1; return { MS_NO_ROLE: 'Nemate ulogu.' }; } });
    asFunction.page.start();
    assert.equal(asFunction.ids.error.textContent, 'Nemate ulogu.');
    assert.equal(asked, 1);
  });

  it('redraws the submit button, the show-password button and the error line when the language changes', async () => {
    let lang = 'en';
    const events = fakeEventTarget();
    const p = signInPage({ fetchImpl: () => json({}, 401), text: () => WORDS[lang], eventTarget: events });
    p.page.start();
    await submitForm(p);
    assert.equal(p.ids.error.textContent, 'Invalid credentials.');

    lang = 'hr';
    events.fire('languageChanged', { lang: 'hr' });

    assert.equal(p.ids.error.textContent, 'Neispravni podaci.');
    assert.equal(p.ids.error.classList.contains('hidden'), false);
    assert.equal(p.ids.submit.textContent, 'Prijava');
    assert.equal(p.ids.reveal.getAttribute('aria-label'), 'Prikaži lozinku');
  });

  it('keeps a hidden error hidden when the language changes', () => {
    let lang = 'en';
    const events = fakeEventTarget();
    const p = signInPage({ text: () => WORDS[lang], eventTarget: events });
    p.page.start();

    lang = 'hr';
    events.fire('languageChanged', { lang: 'hr' });

    assert.equal(p.ids.error.classList.contains('hidden'), true);
  });

  it('listens for languageChanged by default, for the name languageEvent gives, and for nothing when it is null', () => {
    const byDefault = fakeEventTarget();
    signInPage({ eventTarget: byDefault }).page.start();
    assert.deepEqual(Object.keys(byDefault.listeners), ['languageChanged']);

    const named = fakeEventTarget();
    signInPage({ eventTarget: named, languageEvent: 'uiLanguage' }).page.start();
    assert.deepEqual(Object.keys(named.listeners), ['uiLanguage']);

    const off = fakeEventTarget();
    signInPage({ eventTarget: off, languageEvent: null }).page.start();
    assert.deepEqual(Object.keys(off.listeners), []);
  });
});
