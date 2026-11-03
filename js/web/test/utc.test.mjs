// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const { utcInstant } = require('../browser/utc.js');

describe('utcInstant', () => {
  it('reads an offsetless date-time as UTC', () => {
    assert.equal(utcInstant('2026-10-01T10:05:00').getTime(), Date.UTC(2026, 9, 1, 10, 5, 0));
  });

  it('reads an offsetless date-time with fractional seconds as UTC', () => {
    assert.equal(utcInstant('2026-10-01T10:05:00.250').getTime(), Date.UTC(2026, 9, 1, 10, 5, 0, 250));
  });

  it('reads a value with Z as it is', () => {
    assert.equal(utcInstant('2026-10-01T10:05:00Z').getTime(), Date.UTC(2026, 9, 1, 10, 5, 0));
  });

  it('reads a value with an offset as it is', () => {
    assert.equal(utcInstant('2026-10-01T10:05:00+02:00').getTime(), Date.UTC(2026, 9, 1, 8, 5, 0));
    assert.equal(utcInstant('2026-10-01T10:05:00-0500').getTime(), Date.UTC(2026, 9, 1, 15, 5, 0));
  });

  it('keeps a date-only value as the day it is, with no time added to it', () => {
    assert.equal(utcInstant('2026-11-03').getTime(), Date.UTC(2026, 10, 3, 0, 0, 0));
  });

  it('gives null for an empty value', () => {
    assert.equal(utcInstant(null), null);
    assert.equal(utcInstant(undefined), null);
    assert.equal(utcInstant(''), null);
  });

  it('gives null for text that is not a date, and never a guess', () => {
    assert.equal(utcInstant('not a date'), null);
    assert.equal(utcInstant('2026-13-45T99:99:00'), null);
  });
});
