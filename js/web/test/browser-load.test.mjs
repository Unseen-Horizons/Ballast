// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Each browser file, run the way a page loads it: as a classic script in a window, with no CommonJS module object. It must
// install its functions on window.BallastWeb, and must not need Node.

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const BROWSER_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'browser');

function loadInWindow(file) {
  const window = {
    console, URL, URLSearchParams, Promise, Object, Date, Intl, Number, String, Set, Array, JSON, Math, Error, TypeError, Symbol
  };
  window.window = window;
  vm.createContext(window);
  vm.runInContext(fs.readFileSync(path.join(BROWSER_DIR, file), 'utf8'), window, { filename: file });
  return window;
}

describe('browser files, loaded as classic scripts', () => {
  const expected = {
    'html.js': ['escapeHtml', 'safeUrl'],
    'utc.js': ['utcInstant'],
    'problem.js': ['readProblemBody', 'problemMessage'],
    'i18n.js': ['createI18n']
  };

  for (const [file, names] of Object.entries(expected)) {
    it(`${file} installs ${names.join(', ')} on window.BallastWeb`, () => {
      const window = loadInWindow(file);
      assert.ok(window.BallastWeb, 'window.BallastWeb is set');
      for (const name of names) {
        assert.equal(typeof window.BallastWeb[name], 'function', `${name} is a function`);
      }
    });
  }

  it('files that load together share one BallastWeb namespace', () => {
    const window = loadInWindow('html.js');
    vm.runInContext(fs.readFileSync(path.join(BROWSER_DIR, 'utc.js'), 'utf8'), window, { filename: 'utc.js' });
    assert.equal(typeof window.BallastWeb.escapeHtml, 'function');
    assert.equal(typeof window.BallastWeb.utcInstant, 'function');
  });
});
