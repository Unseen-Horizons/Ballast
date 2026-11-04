// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The admin files, loaded the way a console page loads them: @ballast/web first, then the admin files, as classic scripts in one
// window. Each file must install its functions on window.BallastAdmin, and the shell must find window.BallastWeb.

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ADMIN = path.resolve(HERE, '..', 'browser');
const WEB = path.resolve(HERE, '..', '..', 'web', 'browser');

function window() {
  const w = {
    console, URL, URLSearchParams, Promise, Object, Date, Intl, Number, String, Set, Array, JSON, Math, Error, TypeError, Symbol,
    Headers, Response, Uint8Array, TextDecoder, atob, btoa, setTimeout, clearTimeout
  };
  w.window = w;
  vm.createContext(w);
  return w;
}

function run(w, dir, file) {
  vm.runInContext(fs.readFileSync(path.join(dir, file), 'utf8'), w, { filename: file });
}

describe('admin files, loaded as classic scripts after @ballast/web', () => {
  it('install the admin functions on window.BallastAdmin', () => {
    const w = window();
    for (const file of ['html.js', 'utc.js', 'problem.js']) run(w, WEB, file);
    for (const file of ['auth.js', 'shell.js', 'login.js', 'roles.js']) run(w, ADMIN, file);

    assert.equal(typeof w.BallastAdmin.createAuth, 'function');
    assert.equal(typeof w.BallastAdmin.createShell, 'function');
    assert.equal(typeof w.BallastAdmin.createLoginPage, 'function');
    assert.equal(typeof w.BallastAdmin.createRolesPage, 'function');
    assert.equal(typeof w.BallastAdmin.redirectTargetFor, 'function');
    assert.equal(typeof w.BallastAdmin.sortRoles, 'function');
  });

  it('fail with a clear message, not a silent blank, when @ballast/web was not loaded first', () => {
    const w = window();
    run(w, ADMIN, 'auth.js');
    run(w, ADMIN, 'shell.js');
    const auth = w.BallastAdmin.createAuth({
      storage: { getItem: () => 'x', setItem() {}, removeItem() {} },
      location: { href: '', origin: 'https://x.test', pathname: '/admin/' }
    });
    const shell = w.BallastAdmin.createShell({ auth, document: {}, window: {} });
    assert.throws(() => shell.esc('x'), /window\.BallastWeb is missing/);
  });
});
