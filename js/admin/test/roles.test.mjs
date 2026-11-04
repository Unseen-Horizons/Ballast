// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

import assert from 'node:assert/strict';
import path from 'node:path';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { fakeDocument, fakeElement } from './fake-dom.mjs';

const require = createRequire(import.meta.url);
const WEB = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'web', 'browser');
globalThis.BallastWeb = Object.assign({}, require(path.join(WEB, 'html.js')), require(path.join(WEB, 'utc.js')));

const {
  createRolesPage, explainError, permissionBoxes, roleCard, sortRoles, syncBody, syncPanel
} = require('../browser/roles.js');

const esc = (value) => globalThis.BallastWeb.escapeHtml(value);
const labels = { ...{}, forbidden: 'You need the %s permission.', fullAccessBadge: 'Full access', fullAccessNote: 'Passes every permission.',
  riderNote: 'Set by the application.', noPermissions: 'No permissions.', fromDirectory: 'From Microsoft', notInDirectory: 'Not in Microsoft',
  save: 'Save', remove: 'Delete', syncTitle: 'Microsoft directory sync', syncNow: 'Sync now', syncLoading: 'Loading the sync…',
  syncOff: 'Off here.', syncNotRun: 'Not run.', syncRoles: 'Roles gone', problems: 'Problems', secretHint: 'Renew it.',
  result: 'Result', lastRun: 'Last run', lastSuccess: 'last success', usersAssigned: 'Users assigned', appRoles: 'app roles',
  accountsUpdated: 'accounts updated', switchedOff: 'switched off', secretEnds: 'Client secret ends', never: 'never', unknown: 'unknown' };

const PERMISSIONS = [
  { key: 'feeds.view', displayName: 'View feeds', description: 'See feeds.', category: 'Feeds' },
  { key: 'feeds.manage', displayName: 'Manage feeds', description: 'Change feeds.', category: 'Feeds' },
  { key: 'users.view', displayName: 'View users', description: 'See users.', category: 'Users' }
];

const dateTime = (v) => `DT(${v})`;
const date = (v) => `D(${v})`;

describe('sortRoles', () => {
  it('puts the full-access role first, the rider role next, and the rest by name', () => {
    const roles = [{ name: 'Viewer' }, { name: 'User' }, { name: 'Admin' }, { name: 'Editor' }];
    assert.deepEqual(sortRoles(roles, 'User', 'Admin').map((r) => r.name), ['Admin', 'User', 'Editor', 'Viewer']);
  });

  it('matches names regardless of case, and leaves the roles unsorted when there are no special roles', () => {
    assert.deepEqual(sortRoles([{ name: 'b' }, { name: 'A' }], null, null).map((r) => r.name), ['A', 'b']);
    assert.deepEqual(sortRoles([{ name: 'client' }, { name: 'ADMIN' }], null, 'admin').map((r) => r.name), ['ADMIN', 'client']);
  });
});

describe('explainError', () => {
  it('names the permission the page needs for a 403, whose body is empty', () => {
    assert.equal(explainError(Object.assign(new Error('Request failed (403)'), { status: 403 }), 'Manage roles', labels),
      'You need the Manage roles permission.');
  });

  it('shows any other failure as the server wrote it', () => {
    assert.equal(explainError(Object.assign(new Error('Conflict.'), { status: 409 }), 'Manage roles', labels), 'Conflict.');
  });
});

describe('permissionBoxes', () => {
  it('draws the catalogue by category, ticks what the role has, and puts the description on the tooltip', () => {
    const html = permissionBoxes(['feeds.manage'], PERMISSIONS, esc);

    assert.match(html, /<div class="fw-semibold text-muted mt-2 mb-1">Feeds<\/div>/);
    assert.match(html, /data-perm="feeds.manage" checked/);
    assert.doesNotMatch(html, /data-perm="feeds.view" checked/);
    assert.match(html, /title="See feeds\."/);
    assert.equal((html.match(/Feeds<\/div>/g) || []).length, 1, 'a category heading is written once');
  });
});

describe('roleCard', () => {
  const ctx = (extra = {}) => ({ esc, labels, permissions: PERMISSIONS, snapshot: null, riderRole: 'User', fullAccessRole: 'Admin',
    canDelete: true, ...extra });

  it('draws the full-access role as a note, with no boxes', () => {
    const html = roleCard({ key: 'Admin', name: 'Admin', permissions: [] }, ctx());
    assert.match(html, /Full access/);
    assert.doesNotMatch(html, /data-save/);
  });

  it('draws the rider role read only, with its permissions listed', () => {
    const html = roleCard({ key: 'User', name: 'User', permissions: ['feeds.view'] }, ctx());
    assert.match(html, /<li>View feeds<\/li>/);
    assert.doesNotMatch(html, /data-save/);
  });

  it('draws an ordinary role with its boxes, its save button, and a delete button only when the directory no longer lists it', () => {
    const listed = roleCard({ key: 7, name: 'Editor', permissions: [], fromMicrosoft: true }, ctx());
    assert.match(listed, /data-role-key="7"/);
    assert.match(listed, /From Microsoft/);
    assert.match(listed, /data-save/);
    assert.doesNotMatch(listed, /data-delete/);

    const gone = roleCard({ key: 'Old', name: 'Old', permissions: [] }, ctx({ snapshot: { rolesNotInMicrosoft: ['old'] } }));
    assert.match(gone, /Not in Microsoft/);
    assert.match(gone, /data-delete/);

    const noDelete = roleCard({ key: 'Old', name: 'Old', permissions: [] }, ctx({ snapshot: { rolesNotInMicrosoft: ['Old'] }, canDelete: false }));
    assert.doesNotMatch(noDelete, /data-delete/);
  });

  it('escapes a role name written by the directory', () => {
    const html = roleCard({ key: 'x', name: '<img src=x>', permissions: [] }, ctx());
    assert.doesNotMatch(html, /<img src=x>/);
    assert.match(html, /&lt;img src=x&gt;/);
  });
});

describe('syncBody and syncPanel', () => {
  const ctxSync = {
    esc, labels, dateTime, date,
    results: { NotRun: 'Not run here', Off: 'Off', Applied: 'Applied', NotApplied: 'Not applied', Failed: 'Failed' },
    failures: { Token: 'Check the secret.' },
    secretStates: { Ok: ['Ok', 'bg-success'], Soon: ['Ends soon', 'bg-warning'], Expired: ['Expired', 'bg-danger'], Unknown: ['Unknown', 'bg-secondary'] }
  };

  it('says the sync is off, or not run on this copy, without the counts', () => {
    assert.equal(syncBody({ result: 'Off' }, ctxSync), '<p class="text-muted mb-0">Off here.</p>');
    assert.equal(syncBody({ result: 'NotRun' }, ctxSync), '<p class="text-muted mb-0">Not run.</p>');
  });

  it('writes an applied run with its counts, its times and its client secret state', () => {
    const html = syncBody({
      result: 'Applied', lastAttemptUtc: '2026-10-01T10:00:00Z', lastSuccessUtc: null,
      counts: { usersAssigned: 12, appRoles: 3, updated: 2, switchedOff: 1 },
      secretState: 'Soon', secretEndsUtc: '2026-12-01T00:00:00Z'
    }, ctxSync);

    assert.match(html, /Applied/);
    assert.match(html, /DT\(2026-10-01T10:00:00Z\)/);
    assert.match(html, /never/, 'a missing success time reads as never');
    assert.match(html, /Users assigned<\/span> 12/);
    assert.match(html, /<span class="badge bg-warning">Ends soon<\/span>/);
    assert.match(html, /Renew it\./);
  });

  it('writes a failure with the words for its cause and its HTTP status', () => {
    const html = syncBody({ result: 'Failed', failure: 'Token', failureStatus: 401, counts: {}, secretState: 'Ok' }, ctxSync);
    assert.match(html, /Failed — Check the secret\. \(HTTP 401\)/);
  });

  it('lists the problems and the roles the directory no longer lists, as text', () => {
    const html = syncBody({
      result: 'Applied', counts: {}, secretState: 'Ok',
      problems: [{ code: 'DUP', subject: '<b>x</b>' }], rolesNotInMicrosoft: ['Old']
    }, ctxSync);
    assert.match(html, /<span class="font-monospace">DUP<\/span> &lt;b&gt;x&lt;\/b&gt;/);
    assert.match(html, /Old<\/span>|Old ·/);
  });

  it('draws a note from a failed read in place of the body, and a loading line before any answer', () => {
    assert.match(syncPanel(null, 'You need the View users permission.', ctxSync), /alert-danger/);
    assert.match(syncPanel(null, '', ctxSync), /Loading the sync…/);
    assert.match(syncPanel({ result: 'Off' }, '', ctxSync), /data-sync-now/);
  });
});

function pageDom() {
  const content = fakeElement('content');
  const notice = fakeElement('notice');
  const sync = fakeElement('sync');
  const roles = fakeElement('roles');
  const document = fakeDocument({ content, notice, sync, roles });
  return { content, notice, sync, roles, document };
}

function fakeShell({ mountOk = true } = {}) {
  return {
    mounted: null,
    mount(page) { this.mounted = page; return mountOk; },
    esc,
    dateTime,
    date
  };
}

describe('createRolesPage', () => {
  it('needs the shell and an adapter with listRoles and saveRole', () => {
    assert.throws(() => createRolesPage({ adapter: {}, page: {} }), TypeError);
    assert.throws(() => createRolesPage({ shell: fakeShell(), adapter: { listRoles() {} }, page: {} }), TypeError);
  });

  it('draws the roles, their permissions and the sync, and saves the boxes that are ticked', async () => {
    const dom = pageDom();
    const shell = fakeShell();
    const saved = [];
    const adapter = {
      listRoles: async () => [{ key: 3, name: 'Editor', permissions: ['feeds.view'], fromMicrosoft: true }],
      listPermissions: async () => PERMISSIONS,
      saveRole: async (key, permissions) => { saved.push([key, permissions]); },
      syncStatus: async () => ({ result: 'Off' }),
      runSync: async () => ({ result: 'Applied', counts: {}, secretState: 'Ok' })
    };
    const page = createRolesPage({ shell, adapter, page: { active: 'roles', title: 'Staff & roles' }, document: dom.document,
      text: labels, permissionNames: { manage: 'Manage roles' } });

    await page.start();

    assert.deepEqual(shell.mounted, { active: 'roles', title: 'Staff & roles' });
    assert.match(dom.roles.innerHTML, /data-role-key="3"/);
    assert.match(dom.roles.innerHTML, /View feeds/);
    assert.match(dom.sync.innerHTML, /Off here\./);

    // The operator ticks "Manage feeds" and saves the card.
    const card = fakeElement('card', { attributes: { 'data-role-key': '3' } });
    const box = fakeElement('box', { attributes: { 'data-perm': 'feeds.manage' } });
    card.querySelectorAll = () => [box];
    card.querySelector = () => fakeElement('save');
    const target = { closest: (sel) => (sel === '[data-save]' ? { closest: () => card } : null) };
    dom.content.listeners.click[0]({ target });
    await new Promise((resolve) => setTimeout(resolve, 5));

    assert.deepEqual(saved, [[3, ['feeds.manage']]]);
  });

  it('shows the 403 with the permission the page needs, and leaves the roles drawn as an error', async () => {
    const dom = pageDom();
    const adapter = {
      listRoles: async () => { throw Object.assign(new Error('Request failed (403)'), { status: 403 }); },
      listPermissions: async () => PERMISSIONS,
      saveRole: async () => {}
    };
    const page = createRolesPage({ shell: fakeShell(), adapter, page: { active: 'roles', title: 'x' }, document: dom.document,
      text: labels, permissionNames: { view: 'View roles' } });

    await page.start();

    assert.match(dom.roles.innerHTML, /You need the View roles permission\./);
    assert.doesNotMatch(dom.sync.innerHTML, /data-sync-now/, 'a console without a sync adapter draws no sync panel');
  });

  it('draws nothing when the shell refuses the page, because the operator is sent to sign in', async () => {
    const dom = pageDom();
    let listed = false;
    const adapter = { listRoles: async () => { listed = true; return []; }, listPermissions: async () => [], saveRole: async () => {} };
    const page = createRolesPage({ shell: fakeShell({ mountOk: false }), adapter, page: { active: 'roles', title: 'x' }, document: dom.document });

    await page.start();

    assert.equal(listed, false);
  });

  it('asks before deleting a role the directory no longer lists, and then deletes it by its key', async () => {
    const dom = pageDom();
    const deleted = [];
    const adapter = {
      listRoles: async () => [{ key: 'Old', name: 'Old', permissions: [] }],
      listPermissions: async () => PERMISSIONS,
      saveRole: async () => {},
      deleteRole: async (key) => { deleted.push(key); },
      syncStatus: async () => ({ result: 'Applied', counts: {}, secretState: 'Ok', rolesNotInMicrosoft: ['Old'] }),
      runSync: async () => ({ result: 'Applied', counts: {}, secretState: 'Ok', rolesNotInMicrosoft: ['Old'] })
    };
    const page = createRolesPage({ shell: fakeShell(), adapter, page: { active: 'roles', title: 'x' }, document: dom.document });
    await page.start();

    const asked = [];
    globalThis.confirm = (message) => { asked.push(message); return true; };
    try {
      const card = fakeElement('card', { attributes: { 'data-role-key': 'Old' } });
      dom.content.listeners.click[0]({ target: { closest: (sel) => (sel === '[data-delete]' ? { closest: () => card } : null) } });
      await new Promise((resolve) => setTimeout(resolve, 5));
    } finally {
      delete globalThis.confirm;
    }

    assert.match(asked[0], /Delete the role Old\?/);
    assert.deepEqual(deleted, ['Old']);
  });

  it('runs the sync on request and redraws the panel with its result', async () => {
    const dom = pageDom();
    let runs = 0;
    const adapter = {
      listRoles: async () => [],
      listPermissions: async () => PERMISSIONS,
      saveRole: async () => {},
      syncStatus: async () => ({ result: 'NotRun' }),
      runSync: async () => { runs += 1; return { result: 'Applied', counts: { usersAssigned: 4 }, secretState: 'Ok' }; }
    };
    const page = createRolesPage({ shell: fakeShell(), adapter, page: { active: 'roles', title: 'x' }, document: dom.document, text: labels });
    await page.start();

    dom.content.listeners.click[0]({ target: { closest: (sel) => (sel === '[data-sync-now]' ? {} : null) } });
    await new Promise((resolve) => setTimeout(resolve, 5));

    assert.equal(runs, 1);
    assert.match(dom.sync.innerHTML, /Users assigned<\/span> 4/);
  });
});
