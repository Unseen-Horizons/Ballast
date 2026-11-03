// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

import { fakeJwt, fakeLocation, fakeStorage } from './fake-dom.mjs';

const require = createRequire(import.meta.url);
const { createAuth, decodeClaims } = require('../browser/auth.js');

const ORIGIN = 'https://console.example.test';

function json(body, status = 200, headers = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
}

function setup({ tokens = {}, href = `${ORIGIN}/admin/index.html`, fetchImpl } = {}) {
  const storage = fakeStorage(tokens);
  const location = fakeLocation(href);
  const calls = [];
  const fetch = async (input, init) => {
    calls.push({ input, init });
    return fetchImpl ? fetchImpl(input, init, calls.length) : json({ accessToken: 'new-access', refreshToken: 'new-refresh' });
  };
  const auth = createAuth({ storage, location, fetch, refreshTimeoutMs: 50 });
  return { auth, storage, location, calls };
}

describe('decodeClaims', () => {
  it('reads the payload of a token as UTF-8 JSON', () => {
    assert.deepEqual(decodeClaims(fakeJwt({ sub: 'u1', name: 'Čedomir Đurić' })), { sub: 'u1', name: 'Čedomir Đurić' });
  });

  it('gives null for a value that is not a token', () => {
    assert.equal(decodeClaims(''), null);
    assert.equal(decodeClaims('not-a-token'), null);
    assert.equal(decodeClaims('a.!!!.c'), null);
  });
});

describe('createAuth: the tokens', () => {
  it('stores the pair under the console keys, and clears both on sign-out', () => {
    const { auth, storage } = setup();

    auth.setTokens({ accessToken: 'a1', refreshToken: 'r1' });
    assert.equal(storage.getItem('auth_token'), 'a1');
    assert.equal(storage.getItem('refresh_token'), 'r1');
    assert.equal(auth.token(), 'a1');

    auth.clearTokens();
    assert.equal(auth.token(), '');
    assert.equal(storage.getItem('refresh_token'), null);
  });

  it('keeps the refresh token already stored when an answer brings none', () => {
    const { auth, storage } = setup({ tokens: { refresh_token: 'kept' } });
    auth.setTokens({ accessToken: 'a2' });
    assert.equal(storage.getItem('refresh_token'), 'kept');
  });

  it('refuses a token pair without an access token', () => {
    const { auth } = setup();
    assert.throws(() => auth.setTokens({ refreshToken: 'r' }), TypeError);
  });

  it('gives the Authorization header only while there is a token', () => {
    const { auth } = setup({ tokens: { auth_token: 'a1' } });
    assert.equal(auth.headers().Authorization, 'Bearer a1');
    auth.clearTokens();
    assert.equal(auth.headers().Authorization, undefined);
  });
});

describe('createAuth: signing in and out', () => {
  it('remembers the page and opens the sign-in page when there is no token', () => {
    const { auth, storage, location } = setup({ href: `${ORIGIN}/admin/feeds.html` });

    assert.equal(auth.requireSignedIn(), false);
    assert.equal(storage.getItem('redirect_after_login'), `${ORIGIN}/admin/feeds.html`);
    assert.deepEqual(location.navigations, ['/admin/login.html']);
  });

  it('does nothing when there is a token', () => {
    const { auth, location } = setup({ tokens: { auth_token: 'a1' } });
    assert.equal(auth.requireSignedIn(), true);
    assert.deepEqual(location.navigations, []);
  });

  it('sign-out forgets both tokens and opens the sign-in page', () => {
    const { auth, storage, location } = setup({ tokens: { auth_token: 'a1', refresh_token: 'r1' } });
    auth.signOut();
    assert.equal(storage.getItem('auth_token'), null);
    assert.equal(storage.getItem('refresh_token'), null);
    assert.deepEqual(location.navigations, ['/admin/login.html']);
  });
});

describe('createAuth: refresh', () => {
  it('exchanges the stored refresh token and stores the new pair', async () => {
    const { auth, storage, calls } = setup({ tokens: { refresh_token: 'r1' } });

    assert.equal(await auth.refresh(), true);
    assert.equal(calls[0].input, '/auth/refresh');
    assert.deepEqual(JSON.parse(calls[0].init.body), { refreshToken: 'r1' });
    assert.equal(storage.getItem('auth_token'), 'new-access');
    assert.equal(storage.getItem('refresh_token'), 'new-refresh');
  });

  it('resolves to false with no refresh token, and makes no request', async () => {
    const { auth, calls } = setup();
    assert.equal(await auth.refresh(), false);
    assert.equal(calls.length, 0);
  });

  it('resolves to false when the server refuses the token, and keeps the old pair', async () => {
    const { auth, storage } = setup({
      tokens: { auth_token: 'old', refresh_token: 'r1' },
      fetchImpl: () => json({ title: 'Refused' }, 401)
    });
    assert.equal(await auth.refresh(), false);
    assert.equal(storage.getItem('auth_token'), 'old');
  });

  it('resolves to false when the answer is not a token pair', async () => {
    const { auth } = setup({ tokens: { refresh_token: 'r1' }, fetchImpl: () => json({ detail: 'odd' }) });
    assert.equal(await auth.refresh(), false);
  });

  it('resolves to false when the request takes longer than the timeout', async () => {
    const { auth } = setup({ tokens: { refresh_token: 'r1' }, fetchImpl: () => new Promise(() => {}) });
    assert.equal(await auth.refresh(), false);
  });

  it('shares one request between concurrent callers, because the refresh token is single-use', async () => {
    const { auth, calls } = setup({ tokens: { refresh_token: 'r1' } });

    const results = await Promise.all([auth.refresh(), auth.refresh(), auth.refresh()]);

    assert.deepEqual(results, [true, true, true]);
    assert.equal(calls.length, 1);
  });
});

describe('createAuth.install: the console\'s fetch', () => {
  const realFetch = globalThis.fetch;
  let log;

  beforeEach(() => { log = []; });
  afterEach(() => { globalThis.fetch = realFetch; });

  function installWith(state, responses) {
    const base = async (input, init) => {
      log.push({ input, init });
      return responses.shift();
    };
    // install() wraps globalThis.fetch, and reads the function it wraps at install time.
    globalThis.fetch = base;
    const auth = createAuth({ storage: state.storage, location: state.location, fetch: base, refreshTimeoutMs: 50 });
    auth.install();
    return auth;
  }

  it('attaches the token to a same-origin request', async () => {
    const state = setup({ tokens: { auth_token: 'a1' } });
    installWith(state, [json({ ok: true })]);

    const res = await globalThis.fetch('/admin/stats');

    assert.equal(res.status, 200);
    assert.equal(new Headers(log[0].init.headers).get('Authorization'), 'Bearer a1');
  });

  it('never gives the token to another origin', async () => {
    const state = setup({ tokens: { auth_token: 'a1' } });
    installWith(state, [json({})]);

    await globalThis.fetch('https://tiles.example.test/style.json');

    assert.equal(log[0].init, undefined);
  });

  it('renews the token once on a 401 and retries with the new one', async () => {
    const state = setup({ tokens: { auth_token: 'old', refresh_token: 'r1' } });
    installWith(state, [
      json({ title: 'Expired' }, 401),
      json({ accessToken: 'fresh', refreshToken: 'r2' }),
      json({ ok: true })
    ]);

    const res = await globalThis.fetch('/admin/stats');

    assert.equal(res.status, 200);
    assert.equal(new Headers(log[2].init.headers).get('Authorization'), 'Bearer fresh');
    assert.equal(state.storage.getItem('refresh_token'), 'r2');
  });

  it('signs the operator out when the renewal fails', async () => {
    const state = setup({ tokens: { auth_token: 'old', refresh_token: 'r1' } });
    installWith(state, [json({}, 401), json({}, 401)]);

    const res = await globalThis.fetch('/admin/stats');

    assert.equal(res.status, 401);
    assert.equal(state.storage.getItem('auth_token'), null);
    assert.deepEqual(state.location.navigations, ['/admin/login.html']);
  });

  it('retries a 429 once after its Retry-After', async () => {
    const state = setup({ tokens: { auth_token: 'a1' } });
    installWith(state, [json({}, 429, { 'Retry-After': '0' }), json({ ok: true })]);

    const res = await globalThis.fetch('/admin/stats');

    assert.equal(res.status, 200);
    assert.equal(log.length, 2);
  });

  it('opens the sign-in page at once when asked, but never from the sign-in page itself', () => {
    const signedOut = setup({ href: `${ORIGIN}/admin/feeds.html` });
    installWith(signedOut, []);
    assert.deepEqual(signedOut.location.navigations, []);

    const again = setup({ href: `${ORIGIN}/admin/feeds.html` });
    const auth = createAuth({ storage: again.storage, location: again.location, fetch: async () => json({}) });
    auth.install({ redirectWhenSignedOut: true });
    assert.deepEqual(again.location.navigations, ['/admin/login.html']);

    const onLogin = setup({ href: `${ORIGIN}/admin/login.html` });
    const loginAuth = createAuth({ storage: onLogin.storage, location: onLogin.location, fetch: async () => json({}) });
    loginAuth.install({ redirectWhenSignedOut: true });
    assert.deepEqual(onLogin.location.navigations, []);
  });
});
