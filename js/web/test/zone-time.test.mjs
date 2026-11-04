// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The place-time helpers, run under several device zones. Each case starts a Node process with TZ set to the device zone, so
// the device's own clock is the one the helpers see. These are the cases of the time-zone script test they were ported from.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const SCRIPT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'browser', 'zone-time.js');

const PROBE = `
const z = require(process.argv[1]);
const out = {};
out.wall = z.ztToMs('2026-10-01T10:05:00', 'Europe/Zagreb');
out.wallWithSeconds = z.ztToMs('2026-10-01 10:05:30', 'Europe/Zagreb');
out.springBefore = z.ztToMs('2026-03-29T01:30:00', 'Europe/Zagreb');
out.springAfter = z.ztToMs('2026-03-29T04:00:00', 'Europe/Zagreb');
out.autumnAfter = z.ztToMs('2026-10-25T04:00:00', 'Europe/Zagreb');
out.zulu = z.ztToMs('2026-10-01T10:05:00Z', 'Europe/Zagreb');
out.offset = z.ztToMs('2026-10-01T10:05:00+02:00', 'Europe/Zagreb');
out.noZone = z.ztToMs('2026-10-01T10:05:00', null);
out.garbage = z.ztToMs('not a date', 'Europe/Zagreb');
out.shown = z.fmtPlaceTime('2026-10-01T10:05:00', 'Europe/Zagreb');
out.deviceZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
process.stdout.write(JSON.stringify(out));
`;

function runInDeviceZone(deviceZone) {
  const result = spawnSync(process.execPath, ['-e', PROBE, SCRIPT], {
    env: { ...process.env, TZ: deviceZone },
    encoding: 'utf8'
  });
  assert.equal(result.status, 0, `node failed: ${result.stderr}`);
  return JSON.parse(result.stdout);
}

function utc(y, mo, d, h, mi, s = 0) {
  return Date.UTC(y, mo - 1, d, h, mi, s);
}

describe('zone-time: a place wall clock read with a place zone', () => {
  for (const deviceZone of ['Europe/Zagreb', 'America/New_York', 'Asia/Tokyo', 'UTC']) {
    it(`reads an offsetless place string as the place's clock in device zone ${deviceZone}`, () => {
      const o = runInDeviceZone(deviceZone);
      assert.equal(o.deviceZone, deviceZone);
      assert.equal(o.wall, utc(2026, 10, 1, 8, 5, 0));
      assert.equal(o.wallWithSeconds, utc(2026, 10, 1, 8, 5, 30));
    });
  }

  for (const deviceZone of ['Europe/Zagreb', 'America/New_York', 'Asia/Tokyo']) {
    it(`reads wall times on the daylight-saving switch days with the offset in force (device ${deviceZone})`, () => {
      const o = runInDeviceZone(deviceZone);
      // 2026-03-29 in Zagreb: 01:30 is still CET (UTC+1), 04:00 is CEST (UTC+2).
      assert.equal(o.springBefore, utc(2026, 3, 29, 0, 30, 0));
      assert.equal(o.springAfter, utc(2026, 3, 29, 2, 0, 0));
      // 2026-10-25: 04:00 is CET again.
      assert.equal(o.autumnAfter, utc(2026, 10, 25, 3, 0, 0));
    });
  }

  for (const deviceZone of ['America/New_York', 'Asia/Tokyo']) {
    it(`reads instants with Z or an offset whatever the place zone (device ${deviceZone})`, () => {
      const o = runInDeviceZone(deviceZone);
      assert.equal(o.zulu, utc(2026, 10, 1, 10, 5, 0));
      assert.equal(o.offset, utc(2026, 10, 1, 8, 5, 0));
    });
  }

  it('falls back to the device clock when the zone is missing, and returns null for unparseable text', () => {
    const o = runInDeviceZone('Asia/Tokyo');
    // With no zone the string is the device's own wall clock (Tokyo is UTC+9 on that date).
    assert.equal(o.noZone, utc(2026, 10, 1, 1, 5, 0));
    assert.equal(o.garbage, null);
  });

  it('shows the place clock, and carries the offset label only when the device differs', () => {
    const inZagreb = runInDeviceZone('Europe/Zagreb');
    const inNewYork = runInDeviceZone('America/New_York');
    assert.match(inZagreb.shown, /10:05/);
    assert.doesNotMatch(inZagreb.shown, /GMT/);
    assert.match(inNewYork.shown, /10:05/);
    assert.match(inNewYork.shown, /GMT\+2/);
  });
});

describe('zone-time: the browser path', () => {
  it('installs the helpers as globals when the file runs as a classic script', async () => {
    const vm = await import('node:vm');
    const fs = await import('node:fs');
    const sandbox = { Intl, Date, Math, Number, String, Set, Array, JSON, Object, Promise };
    vm.createContext(sandbox);
    vm.runInContext(fs.readFileSync(SCRIPT, 'utf8'), sandbox);
    assert.equal(typeof sandbox.fmtPlaceTime, 'function');
    assert.equal(typeof sandbox.fmtEventTime, 'function');
    assert.equal(typeof sandbox.ztToMs, 'function');
    assert.equal(sandbox.ztToMs('2026-10-01T10:05:00Z', null), utc(2026, 10, 1, 10, 5, 0));
  });
});
