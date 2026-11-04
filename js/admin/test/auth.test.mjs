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

function setup({ tokens = {}, href = `${ORIGIN}/admin/index.html`, fetchImpl, logoutPath } = {}) {
  const storage = fakeStorage(tokens);
  const location = fakeLocation(href);
  const calls = [];
  const fetch = async (input, init) => {
    calls.push({ input, init });
    return fetchImpl ? fetchImpl(input, init, calls.length) : json({ accessToken: 'new-access', refreshToken: 'new-refresh' });
  };
  const auth = createAuth({ storage, location, fetch, refreshTimeoutMs: 50, logoutPath });
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

describe('createAuth: signing out with the server', () => {
  it('sends the stored access token as the bearer of the logout request', async () => {
    const { auth, calls } = setup({
      tokens: { auth_token: 'a1', refresh_token: 'r1' },
      logoutPath: '/auth/logout',
      fetchImpl: () => new Response(null, { status: 204 })
    });

    assert.equal(await auth.signOut(), true);
    assert.equal(new Headers(calls[0].init.headers).get('Authorization'), 'Bearer a1');
    assert.equal(new Headers(calls[0].init.headers).get('Content-Type'), 'application/json');
  });

  it('sends no Authorization header when no access token is stored, and still posts the refresh token', async () => {
    const { auth, calls } = setup({
      tokens: { refresh_token: 'r1' },
      logoutPath: '/auth/logout',
      fetchImpl: () => new Response(null, { status: 204 })
    });

    assert.equal(await auth.signOut(), true);
    assert.equal(new Headers(calls[0].init.headers).get('Authorization'), null);
    assert.deepEqual(JSON.parse(calls[0].init.body), { refreshToken: 'r1' });
  });

  it('a 401 from the logout endpoint still clears the local session and ends signed out', async () => {
    const { auth, storage, location } = setup({
      tokens: { auth_token: 'a1', refresh_token: 'r1' },
      logoutPath: '/auth/logout',
      fetchImpl: () => json({ title: 'Unauthorized' }, 401)
    });

    assert.equal(await auth.signOut(), false);
    assert.equal(storage.getItem('auth_token'), null);
    assert.equal(storage.getItem('refresh_token'), null);
    assert.deepEqual(location.navigations, ['/admin/login.html']);
  });

  it('posts the refresh token to logoutPath with keepalive, then clears both tokens and opens the sign-in page', async () => {
    const { auth, storage, location, calls } = setup({
      tokens: { auth_token: 'a1', refresh_token: 'r1' },
      logoutPath: '/auth/logout',
      fetchImpl: () => new Response(null, { status: 204 })
    });

    assert.equal(await auth.signOut(), true);
    assert.equal(calls[0].input, '/auth/logout');
    assert.equal(calls[0].init.method, 'POST');
    assert.equal(calls[0].init.keepalive, true);
    assert.deepEqual(JSON.parse(calls[0].init.body), { refreshToken: 'r1' });
    assert.equal(storage.getItem('auth_token'), null);
    assert.equal(storage.getItem('refresh_token'), null);
    assert.deepEqual(location.navigations, ['/admin/login.html']);
  });

  it('clears the tokens and opens the sign-in page when the server refuses the sign-out', async () => {
    const { auth, storage, location } = setup({
      tokens: { auth_token: 'a1', refresh_token: 'r1' },
      logoutPath: '/auth/logout',
      fetchImpl: () => json({ title: 'Refused' }, 500)
    });

    assert.equal(await auth.signOut(), false);
    assert.equal(storage.getItem('auth_token'), null);
    assert.equal(storage.getItem('refresh_token'), null);
    assert.deepEqual(location.navigations, ['/admin/login.html']);
  });

  it('clears the tokens when the request fails on the network', async () => {
    const { auth, storage, location } = setup({
      tokens: { auth_token: 'a1', refresh_token: 'r1' },
      logoutPath: '/auth/logout',
      fetchImpl: () => { throw new TypeError('offline'); }
    });

    assert.equal(await auth.signOut(), false);
    assert.equal(storage.getItem('auth_token'), null);
    assert.deepEqual(location.navigations, ['/admin/login.html']);
  });

  it('clears the tokens when the server does not answer within refreshTimeoutMs', async () => {
    const { auth, storage, location } = setup({
      tokens: { auth_token: 'a1', refresh_token: 'r1' },
      logoutPath: '/auth/logout',
      fetchImpl: () => new Promise(() => {})
    });

    assert.equal(await auth.signOut(), false);
    assert.equal(storage.getItem('auth_token'), null);
    assert.deepEqual(location.navigations, ['/admin/login.html']);
  });

  it('posts nothing without a refresh token, and posts nothing at all when logoutPath is null (the default)', async () => {
    const noRefresh = setup({ tokens: { auth_token: 'a1' }, logoutPath: '/auth/logout' });
    assert.equal(await noRefresh.auth.signOut(), true);
    assert.equal(noRefresh.calls.length, 0);
    assert.equal(noRefresh.storage.getItem('auth_token'), null);

    const noPath = setup({ tokens: { auth_token: 'a1', refresh_token: 'r1' } });
    noPath.auth.signOut();
    assert.equal(noPath.calls.length, 0);
    assert.equal(noPath.storage.getItem('auth_token'), null);
    assert.equal(noPath.storage.getItem('refresh_token'), null);
    assert.deepEqual(noPath.location.navigations, ['/admin/login.html']);
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

describe('createAuth.install with no fetch option: the refresh is never renewed by the wrapper', () => {
  const realFetch = globalThis.fetch;
  afterEach(() => { globalThis.fetch = realFetch; });

  // install() wraps globalThis.fetch. No fetch option is passed, so the refresh must use the fetch that was in place before the wrap.
  function installDefault(tokens, base) {
    globalThis.fetch = base;
    const storage = fakeStorage(tokens);
    const location = fakeLocation(`${ORIGIN}/admin/feeds.html`);
    const auth = createAuth({ storage, location, refreshTimeoutMs: 15000 });
    auth.install();
    return { auth, storage, location };
  }

  it('auth.refresh() after install() resolves false at once when the server refuses', async () => {
    const { auth } = installDefault({ refresh_token: 'r1' }, async () => json({ title: 'Refused' }, 401));

    const started = Date.now();
    assert.equal(await auth.refresh(), false);
    assert.ok(Date.now() - started < 2000, 'a refused refresh must not wait for the timeout');
  });

  it('gives a refused refresh at once, not at the timeout, and renews nothing', async () => {
    const seen = [];
    const base = async (input) => {
      seen.push(input);
      return json({ title: 'Refused' }, 401);
    };
    const { storage, location } = installDefault({ auth_token: 'old', refresh_token: 'r1' }, base);

    const started = Date.now();
    const res = await globalThis.fetch('/admin/stats');

    assert.equal(res.status, 401);
    assert.ok(Date.now() - started < 2000, 'a refused refresh must not wait for the timeout');
    assert.deepEqual(seen, ['/admin/stats', '/auth/refresh']);
    assert.equal(storage.getItem('auth_token'), null);
    assert.deepEqual(location.navigations, ['/admin/login.html']);
  });

  it('renews a 401 once: one refresh, then one replay with the new token', async () => {
    const seen = [];
    const base = async (input, init) => {
      seen.push(input);
      if (input === '/auth/refresh') return json({ accessToken: 'fresh', refreshToken: 'r2' });
      const bearer = new Headers(init?.headers || {}).get('Authorization');
      return bearer === 'Bearer fresh' ? json({ ok: true }) : json({ title: 'Expired' }, 401);
    };
    const { storage } = installDefault({ auth_token: 'old', refresh_token: 'r1' }, base);

    const res = await globalThis.fetch('/admin/stats');

    assert.equal(res.status, 200);
    assert.deepEqual(seen, ['/admin/stats', '/auth/refresh', '/admin/stats']);
    assert.equal(storage.getItem('refresh_token'), 'r2');
  });

  it('shares one refresh between two concurrent 401s', async () => {
    const seen = [];
    const base = async (input, init) => {
      seen.push(input);
      if (input === '/auth/refresh') {
        await new Promise((resolve) => setTimeout(resolve, 10));
        return json({ accessToken: 'fresh', refreshToken: 'r2' });
      }
      const bearer = new Headers(init?.headers || {}).get('Authorization');
      return bearer === 'Bearer fresh' ? json({ ok: true }) : json({ title: 'Expired' }, 401);
    };
    installDefault({ auth_token: 'old', refresh_token: 'r1' }, base);

    const results = await Promise.all([globalThis.fetch('/admin/a'), globalThis.fetch('/admin/b')]);

    assert.deepEqual(results.map((res) => res.status), [200, 200]);
    assert.equal(seen.filter((input) => input === '/auth/refresh').length, 1);
  });
});
