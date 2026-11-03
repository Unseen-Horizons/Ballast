// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const { escapeHtml, safeUrl } = require('../browser/html.js');

describe('escapeHtml', () => {
  it('escapes the five characters that matter, quotes included', () => {
    assert.equal(escapeHtml(`<a href="x" title='y'>&</a>`),
      '&lt;a href=&quot;x&quot; title=&#39;y&#39;&gt;&amp;&lt;/a&gt;');
  });

  it('escapes a double quote, so a value is safe inside a double-quoted attribute', () => {
    assert.equal(escapeHtml('" onmouseover="alert(1)'), '&quot; onmouseover=&quot;alert(1)');
  });

  it('escapes a single quote, so a value is safe inside a single-quoted attribute', () => {
    assert.equal(escapeHtml("' onclick='x"), '&#39; onclick=&#39;x');
  });

  it('gives an empty string for null and undefined', () => {
    assert.equal(escapeHtml(null), '');
    assert.equal(escapeHtml(undefined), '');
  });

  it('turns numbers and booleans into their text', () => {
    assert.equal(escapeHtml(0), '0');
    assert.equal(escapeHtml(false), 'false');
  });

  it('leaves text without special characters, and non-ASCII letters, as they are', () => {
    assert.equal(escapeHtml('Čakovec, Đurđevac'), 'Čakovec, Đurđevac');
  });

  it('escapes once, so an already escaped value is escaped again', () => {
    assert.equal(escapeHtml('&amp;'), '&amp;amp;');
  });
});

describe('safeUrl', () => {
  const base = 'https://admin.example.test';

  it('keeps an http or https address as it was written', () => {
    assert.equal(safeUrl('https://example.test/a?b=c', base), 'https://example.test/a?b=c');
    assert.equal(safeUrl('http://example.test/', base), 'http://example.test/');
  });

  it('keeps a relative address, resolved against the base', () => {
    assert.equal(safeUrl('/admin/feeds.html', base), '/admin/feeds.html');
  });

  it('refuses a javascript: address', () => {
    assert.equal(safeUrl('javascript:alert(1)', base), '#');
    assert.equal(safeUrl('JavaScript:alert(1)', base), '#');
  });

  it('refuses a data: address and a mailto: address', () => {
    assert.equal(safeUrl('data:text/html;base64,PHNjcmlwdD4=', base), '#');
    assert.equal(safeUrl('mailto:someone@example.test', base), '#');
  });

  it('gives # for an empty value', () => {
    assert.equal(safeUrl('', base), '#');
    assert.equal(safeUrl(null, base), '#');
  });

  it('gives # for a value that does not parse against the base', () => {
    assert.equal(safeUrl('http://', base), '#');
  });

  it('gives # for a relative value when there is no base to resolve it against', () => {
    assert.equal(safeUrl('/admin/feeds.html', undefined), '#');
  });
});
