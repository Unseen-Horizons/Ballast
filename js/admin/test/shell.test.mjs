// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

import assert from 'node:assert/strict';
import path from 'node:path';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { fakeDocument, fakeElement, fakeJwt, fakeLocation, fakeStorage } from './fake-dom.mjs';

const require = createRequire(import.meta.url);
const WEB = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'web', 'browser');
// The web helpers the shell reads, as a page would have them on window.BallastWeb.
globalThis.BallastWeb = Object.assign({}, require(path.join(WEB, 'html.js')), require(path.join(WEB, 'utc.js')),
  require(path.join(WEB, 'problem.js')));

const { createAuth } = require('../browser/auth.js');
const { createShell } = require('../browser/shell.js');

const ORIGIN = 'https://console.example.test';

function json(body, status = 200, headers = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
}

const NAV = [
  { label: 'MONITOR', items: [
    { key: 'overview', text: 'Overview', href: '/admin/index.html' },
    { key: 'feeds', text: 'Feeds', href: '/admin/feeds.html', count: 'feeds' },
    { key: 'users', text: 'Users', href: '/admin/users.html', permission: 'users.view' }
  ] },
  { label: 'AI', items: [
    { key: 'ai', text: 'AI', href: '/admin/ai.html', anyPermission: ['ai.view', 'ai.manage'] }
  ] }
];

// One fetch serves both the shell and the sign-in state, so a refresh is counted with the calls around it.
function harness({ signedIn = true, email = 'ana@example.test', hostname = 'console.example.test', fetchImpl, shellOptions = {} } = {}) {
  const storage = fakeStorage(signedIn ? { auth_token: fakeJwt({ sub: 'u1', email }), refresh_token: 'r1' } : {});
  const location = fakeLocation(`${ORIGIN}/admin/index.html`);
  Object.defineProperty(location, 'hostname', { value: hostname });
  const calls = [];
  const fetch = async (input, init) => {
    calls.push({ input, init });
    return fetchImpl ? fetchImpl(input, init, calls.length) : json({});
  };
  const auth = createAuth({ storage, location, fetch, refreshTimeoutMs: 50 });
  const sessionStorage = fakeStorage();
  const avatar = fakeElement('railAvatar');
  const shellHost = fakeElement('shell');
  const pagination = fakeElement('pagination');
  const content = fakeElement('content');
  const document = fakeDocument({ shell: shellHost, railAvatar: avatar, pagination, content });
  const window = { location, confirm: () => true, hash: '' };
  const shell = createShell({
    auth, nav: NAV, brand: { mark: 'G', name: 'Console', sub: 'private' }, document, window,
    storage: sessionStorage, fetch, countsDelayMs: 0, highlightHash: false, ...shellOptions
  });
  return { shell, auth, storage, location, document, avatar, shellHost, content, pagination, sessionStorage, calls, window };
}

describe('createShell: the sign-in state and the page', () => {
  it('needs the sign-in state', () => {
    assert.throws(() => createShell({}), TypeError);
  });

  it('draws the rail and top bar with the brand, the navigation and the person\'s initials', () => {
    const h = harness();
    assert.equal(h.shell.mount({ active: 'feeds', title: 'Feeds & more' }), true);

    const html = h.shellHost.innerHTML;
    assert.match(html, /<b>Console<\/b><span>private<\/span>/);
    assert.match(html, /<a class="nav-item is-active" href="\/admin\/feeds.html">/);
    assert.match(html, /<h1>Feeds &amp; more<\/h1>/);
    assert.match(h.shellHost.innerHTML, /id="railAvatar" title="ana@example.test — sign out">AN</);
    assert.equal(h.shell.initials('ana@example.test'), 'AN');
  });

  it('draws the environment badge: LOCAL on a local host, PRODUCTION elsewhere, and no badge when turned off', () => {
    const local = harness({ hostname: 'localhost' });
    local.shell.mount({ active: 'feeds', title: 'x' });
    assert.match(local.shellHost.innerHTML, /<div class="env-badge">LOCAL<\/div>/);

    const production = harness({ hostname: 'console.example.test' });
    production.shell.mount({ active: 'feeds', title: 'x' });
    assert.match(production.shellHost.innerHTML, /<div class="env-badge">PRODUCTION<\/div>/);

    const off = harness({ shellOptions: { showEnvironment: false } });
    off.shell.mount({ active: 'feeds', title: 'x' });
    assert.doesNotMatch(off.shellHost.innerHTML, /env-badge/);
  });

  it('writes the footer markup the console gives, as it is', () => {
    const h = harness({ shellOptions: { footerHtml: '<div class="mono">one-way</div>' } });
    h.shell.mount({ active: 'feeds', title: 'x' });
    assert.match(h.shellHost.innerHTML, /<div class="rail-foot"><div class="mono">one-way<\/div>/);
  });

  it('escapes the brand and the navigation text it is given', () => {
    const h = harness({ shellOptions: { brand: { name: '<img src=x onerror=alert(1)>' } } });
    h.shell.mount({ active: 'x', title: 'x' });
    assert.doesNotMatch(h.shellHost.innerHTML, /<img src=x/);
    assert.match(h.shellHost.innerHTML, /&lt;img src=x onerror=alert\(1\)&gt;/);
  });

  it('sends a person who is not signed in to the sign-in page, and draws nothing', () => {
    const h = harness({ signedIn: false });
    assert.equal(h.shell.mount({ active: 'feeds', title: 'x' }), false);
    assert.equal(h.shellHost.innerHTML, '');
    assert.deepEqual(h.location.navigations, ['/admin/login.html']);
  });

  it('signs out from the avatar, after a confirmation', () => {
    const h = harness();
    h.shell.mount({ active: 'feeds', title: 'x' });
    h.avatar.dispatch('click');
    assert.deepEqual(h.location.navigations, ['/admin/login.html']);
    assert.equal(h.storage.getItem('auth_token'), null);
  });

  it('mounts the content area that the page fills', () => {
    const h = harness();
    h.shell.mount({ active: 'feeds', title: 'x' });
    assert.match(h.shellHost.innerHTML, /<div class="content" id="content"><\/div>/);
  });

  it('moves the markup of a legacy page into the shell, keeping its elements', () => {
    const h = harness();
    const page = fakeElement('page');
    const existing = fakeElement('child');
    page.appendChild(existing);
    h.document.addElement('page', page);
    const legacyTarget = fakeElement('legacy');
    h.document.bySelector['[data-legacy-content]'] = legacyTarget;

    assert.equal(h.shell.mountLegacy({ active: 'feeds', title: 'x' }), true);

    assert.equal(legacyTarget.children[0], existing, 'the legacy markup moved into the shell, element and all');
    assert.equal(page.firstChild, null);
    assert.equal(page.removed, true);
    assert.match(h.shellHost.innerHTML, /data-legacy-content/);
    assert.equal(h.document.body.classList.contains('has-shell'), true);
  });
});

describe('createShell: permissions in the navigation', () => {
  it('draws every item until the permissions are known, then only the ones the person may use', () => {
    const h = harness();
    h.shell.mount({ active: 'feeds', title: 'x' });
    assert.match(h.shellHost.innerHTML, /href="\/admin\/users.html"/);

    h.shell.setPermissions(['ai.view'], false);
    assert.equal(h.shell.can('users.view'), false);
    assert.equal(h.shell.can('ai.manage'), false);
  });

  it('gives every permission to a person with full access', () => {
    const h = harness();
    h.shell.setPermissions([], true);
    assert.equal(h.shell.can('anything.at.all'), true);
  });

  it('redraws the navigation with the permissions read from the me endpoint', async () => {
    const h = harness({
      shellOptions: { mePath: '/admin/me' },
      fetchImpl: () => json({ permissions: ['users.view'], fullAccess: false })
    });
    const nav = fakeElement('nav');
    h.document.bySelector['.rail-nav'] = nav;
    h.shell.mount({ active: 'feeds', title: 'x' });

    const permissions = await h.shell.loadPermissions();

    assert.deepEqual(permissions, ['users.view']);
    assert.equal(h.calls[0].input, '/admin/me');
    assert.match(nav.innerHTML, /href="\/admin\/users.html"/);
    assert.doesNotMatch(nav.innerHTML, /href="\/admin\/ai.html"/);
  });

  it('does not read permissions when there is no me endpoint', async () => {
    const h = harness();
    assert.equal(await h.shell.loadPermissions(), null);
    assert.equal(h.calls.length, 0);
  });
});

describe('createShell.api', () => {
  it('gives the answer of a successful call, and null for no content', async () => {
    const ok = harness({ fetchImpl: () => json({ total: 3 }) });
    assert.deepEqual(await ok.shell.api('/admin/stats'), { total: 3 });
    assert.equal(new Headers(ok.calls[0].init.headers).get('Authorization'), `Bearer ${fakeJwt({ sub: 'u1', email: 'ana@example.test' })}`);

    const none = harness({ fetchImpl: () => new Response(null, { status: 204 }) });
    assert.equal(await none.shell.api('/admin/x', { method: 'DELETE' }), null);
  });

  it('throws the server\'s own words, with the status on the error', async () => {
    const h = harness({ fetchImpl: () => json({ title: 'Forbidden', detail: 'You need the Manage roles permission.' }, 403) });
    await assert.rejects(() => h.shell.api('/admin/roles'), (error) => {
      assert.equal(error.message, 'You need the Manage roles permission.');
      assert.equal(error.status, 403);
      return true;
    });
  });

  it('appends the field messages of a validation failure', async () => {
    const h = harness({ fetchImpl: () => json({ title: 'One or more validation errors occurred.', errors: { Name: ['Required.'] } }, 400) });
    await assert.rejects(() => h.shell.api('/admin/x', { method: 'POST' }), /Name: Required\./);
  });

  it('says the status when a failed answer has no body', async () => {
    const h = harness({ fetchImpl: () => new Response('', { status: 500 }) });
    await assert.rejects(() => h.shell.api('/admin/x'), (error) => error.message === 'Request failed (500)' && error.status === 500);
  });

  it('renews the session once on a 401, then retries the call', async () => {
    const h = harness({
      fetchImpl: (input, init, n) => {
        if (n === 1) return json({}, 401);
        if (n === 2) return json({ accessToken: 'fresh', refreshToken: 'r2' });
        return json({ ok: true });
      }
    });
    assert.deepEqual(await h.shell.api('/admin/stats'), { ok: true });
    assert.equal(h.storage.getItem('refresh_token'), 'r2');
  });

  it('signs out and throws when the session cannot be renewed', async () => {
    const h = harness({ fetchImpl: () => json({}, 401) });
    await assert.rejects(() => h.shell.api('/admin/stats'), /Session expired\./);
    assert.deepEqual(h.location.navigations, ['/admin/login.html']);
  });

  it('gives null from tryApi for a failed call, and never throws', async () => {
    const h = harness({ fetchImpl: () => json({ title: 'Nope' }, 500) });
    assert.equal(await h.shell.tryApi('/admin/x'), null);
  });

  it('retries a 429 once after its Retry-After, waiting at most 3 seconds', async () => {
    const h = harness({
      fetchImpl: (input, init, n) => (n === 1 ? json({}, 429, { 'Retry-After': '1' }) : json({ ok: true }))
    });
    assert.deepEqual(await h.shell.api('/admin/stats'), { ok: true });
    assert.equal(h.calls.length, 2);
  });

  it('does not retry a 429 whose Retry-After is over 10 seconds, and says when to try again', async () => {
    const h = harness({ fetchImpl: () => json({}, 429, { 'Retry-After': '30' }) });
    await assert.rejects(() => h.shell.api('/admin/stats'), /retry in 30s/);
    assert.equal(h.calls.length, 1);
  });

  it('gives the HTTP status as the message of an answer with no body, for httpErrorMessage', () => {
    const h = harness();
    assert.equal(h.shell.httpErrorMessage(500, undefined), 'HTTP 500');
    assert.equal(h.shell.httpErrorMessage(409, { detail: 'Already done.', traceId: 't9' }), 'Already done. (ref t9)');
  });

  it('gives the full text by default, with the field messages and the trace reference', async () => {
    const body = { title: 'One or more validation errors occurred.', errors: { Name: ['Required.'] }, traceId: 't7' };
    const h = harness({ fetchImpl: () => json(body, 400) });
    await assert.rejects(() => h.shell.api('/admin/x', { method: 'POST' }),
      (error) => error.message === 'One or more validation errors occurred. — Name: Required. (ref t7)');
  });

  it('applies the problem option to the call banner: fieldErrors and traceId off leave the title', async () => {
    const body = { title: 'One or more validation errors occurred.', errors: { Name: ['Required.'] }, traceId: 't7' };
    const h = harness({
      shellOptions: { problem: { fieldErrors: false, traceId: false } },
      fetchImpl: () => json(body, 400)
    });
    await assert.rejects(() => h.shell.api('/admin/x', { method: 'POST' }),
      (error) => error.message === 'One or more validation errors occurred.' && error.status === 400);
  });

  it('applies the problem option to httpErrorMessage too, and a problem fallback replaces the HTTP status text', () => {
    const off = harness({ shellOptions: { problem: { traceId: false } } });
    assert.equal(off.shell.httpErrorMessage(409, { detail: 'Already done.', traceId: 't9' }), 'Already done.');

    const fallback = harness({ shellOptions: { problem: { fallback: 'Try again.' } } });
    assert.equal(fallback.shell.httpErrorMessage(500, undefined), 'Try again.');
  });
});

describe('createShell: banners, pagination and counts', () => {
  it('shows an error in the page banner and hides the spinner', () => {
    const h = harness();
    const loading = fakeElement('loading');
    const error = fakeElement('error', { classes: ['d-none'] });
    h.document.addElement('loading', loading);
    h.document.addElement('error', error);

    h.shell.showError('Could not load.');

    assert.equal(error.textContent, 'Could not load.');
    assert.equal(error.classList.contains('d-none'), false);
    assert.equal(loading.classList.contains('d-none'), true);
  });

  it('shows the error in the content area when the page has no error banner', () => {
    const h = harness();
    h.shell.showError('Lost in content.');
    assert.match(h.content.innerHTML, /Lost in content\./);
  });

  it('draws a pager with the page buttons, and calls back with the page chosen', () => {
    const h = harness();
    h.shell.renderPagination({ page: 2, totalPages: 5, summary: '120 rows' }, (n) => { h.chosen = n; });

    assert.match(h.pagination.innerHTML, /data-page="3"/);
    assert.match(h.pagination.innerHTML, /Page 2 of 5 \(120 rows\)/);
    h.pagination.onclick({ target: { closest: () => ({ getAttribute: () => '3', closest: () => null }) } });
    assert.equal(h.chosen, 3);
  });

  it('hides the pager when the page says there is nothing to page through', () => {
    const h = harness();
    h.shell.renderPagination({ page: 1, totalPages: 1, hidden: true, summary: '' }, () => {});
    assert.equal(h.pagination.classList.contains('d-none'), true);
  });

  it('keeps counts for the next page, merged, and draws the ones it is given', async () => {
    const h = harness({ shellOptions: { counts: async () => ({ feeds: 1200 }) } });
    const feeds = fakeElement('feeds');
    h.document.bySelectorAll['[data-count="feeds"]'] = [feeds];

    h.shell.mount({ active: 'feeds', title: 'x' });
    await new Promise((resolve) => setTimeout(resolve, 10));
    assert.equal(feeds.textContent, '1 200');

    h.shell.applyCounts({ lines: 4 });
    const stored = JSON.parse(h.sessionStorage.getItem('_shellCounts'));
    assert.deepEqual(stored, { feeds: 1200, lines: 4 });
  });

  it('keeps the figure already drawn when a count is null (its read failed)', () => {
    const h = harness();
    const el = fakeElement('count');
    h.document.bySelectorAll['[data-count="feeds"]'] = [el];
    h.shell.applyCounts({ feeds: 7 });
    h.shell.applyCounts({ feeds: null });
    assert.equal(el.textContent, '7');
  });
});

describe('createShell: number and date text', () => {
  it('writes numbers with spaces for the thousands, and a dash for none', () => {
    const h = harness();
    assert.equal(h.shell.num(1234567), '1 234 567');
    assert.equal(h.shell.num(null), '—');
  });

  it('writes an empty instant as a dash, and a date-only value as its day', () => {
    const h = harness();
    assert.equal(h.shell.date(null), '—');
    assert.equal(h.shell.date('2026-11-03'), new Date('2026-11-03').toLocaleDateString('en-GB'));
    assert.equal(h.shell.dateTime('not a date'), '—');
  });

  it('writes a recent instant as minutes or hours ago', () => {
    const h = harness();
    const fiveMinutesAgo = new Date(Date.now() - 5 * 60000).toISOString();
    assert.equal(h.shell.ago(fiveMinutesAgo), '5 min ago');
    assert.equal(h.shell.ago(new Date().toISOString()), 'just now');
  });
});
