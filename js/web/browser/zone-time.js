// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Time helpers for pages that show times of two kinds, and which the page must never mix up.
//
// A place time is when something happens at a place: a departure, the end of a leg, a service day. It is shown in the zone of
// the place it describes, never in the device's zone. An event time is when something happened to the system: a poll, a check,
// a "last updated". It is shown in the viewer's zone. The two kinds use different functions here, so the difference shows at
// every call site.
//
// The zone argument (tz) is an IANA zone id, or null. A null zone is a missing zone. A missing zone is never guessed: the time
// is shown in the device's zone WITH the device's offset label, so a guess is never presented as the place's clock.
//
// This file is a classic <script>. Its functions are globals in a browser, which is how the page scripts call them. Under
// CommonJS (Node tests) it exports them instead.

function ztDeviceZone() {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

// A date-time with no offset: a place's scheduled and arrival times are the place's wall clock ("2026-10-01T10:05:00", with no
// zone on the server). ECMAScript reads such a string as the DEVICE's local time, which shifts it by the difference between the
// place's zone and the device's, so with a place zone it is read as a wall time in that zone instead. Strings with Z or an
// offset are instants and parse as they always did.
const ZT_WALL = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.\d+)?)?$/;

function ztToMs(iso, tz) {
  if (iso instanceof Date) {
    const ms = iso.getTime();
    return Number.isFinite(ms) ? ms : null;
  }
  if (tz && typeof iso === 'string') {
    const w = ZT_WALL.exec(iso.trim());
    if (w) {
      const base = Date.parse(wallToUtcIso(`${w[1]}-${w[2]}-${w[3]}`, `${w[4]}:${w[5]}`, tz));
      return Number.isFinite(base) ? base + Number(w[6] || 0) * 1000 : null;
    }
  }
  const ms = new Date(iso).getTime();
  return Number.isFinite(ms) ? ms : null;
}

// The zone's parts at an instant, as numbers. h23 so midnight is 0, never 24.
function ztParts(ms, tz) {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: tz, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric',
    hour: 'numeric', minute: 'numeric', second: 'numeric'
  }).formatToParts(new Date(ms));
  const o = {};
  for (const p of parts) if (p.type !== 'literal') o[p.type] = Number(p.value);
  return { y: o.year, m: o.month, d: o.day, hh: o.hour % 24, mm: o.minute, ss: o.second };
}

// The zone's UTC offset at an instant, in ms: the zone's wall clock read as UTC, minus the instant.
function ztOffsetMs(ms, tz) {
  const p = ztParts(ms, tz);
  const floored = Math.floor(ms / 1000) * 1000;
  return Date.UTC(p.y, p.m - 1, p.d, p.hh, p.mm, p.ss) - floored;
}

function ztOffsetLabel(ms, tz) {
  const part = new Intl.DateTimeFormat('en-US', { timeZone: tz, timeZoneName: 'shortOffset' })
    .formatToParts(new Date(ms)).find(p => p.type === 'timeZoneName');
  return part ? part.value : '';
}

// "10:05", plus " GMT+1" when the place's offset differs from the device's at that instant, or when the place zone is unknown
// (null) and the device zone stands in.
function fmtPlaceTime(iso, tz) {
  const ms = ztToMs(iso, tz);
  if (ms === null) return '';
  const dev = ztDeviceZone();
  const zone = tz || dev;
  const clock = new Intl.DateTimeFormat([], { hour: '2-digit', minute: '2-digit', timeZone: zone }).format(new Date(ms));
  const differs = !tz || ztOffsetMs(ms, zone) !== ztOffsetMs(ms, dev);
  return differs ? `${clock} ${ztOffsetLabel(ms, zone)}` : clock;
}

// Like fmtPlaceTime, but always with the zone's offset label. For a time shown in a zone that only stands in for the place's own
// (a trip-level zone on a stop that has no zone of its own), so it never reads as the device's clock, even when the offsets match.
function fmtPlaceTimeLabelled(iso, tz) {
  const ms = ztToMs(iso, tz);
  if (ms === null) return '';
  const zone = tz || ztDeviceZone();
  const clock = new Intl.DateTimeFormat([], { hour: '2-digit', minute: '2-digit', timeZone: zone }).format(new Date(ms));
  return `${clock} ${ztOffsetLabel(ms, zone)}`;
}

// {y, m, d, hh, mm} of an instant in the place's zone (the device's zone when tz is null).
function placeParts(iso, tz) {
  const ms = ztToMs(iso, tz);
  if (ms === null) return null;
  const p = ztParts(ms, tz || ztDeviceZone());
  return { y: p.y, m: p.m, d: p.d, hh: p.hh, mm: p.mm };
}

// The wall clock "now" in the place's zone, in the formats <input type=date|time> take.
function placeNowWall(tz) {
  const p = placeParts(Date.now(), tz);
  const z = (n) => String(n).padStart(2, '0');
  return { date: `${p.y}-${z(p.m)}-${z(p.d)}`, time: `${z(p.hh)}:${z(p.mm)}` };
}

// A wall time in tz as an ISO-Z instant. Two offsets are tried (a day either side of the wall time, which brackets any
// transition); a candidate is valid when it reads back as the same wall time. Overlap (two valid candidates) gives the earlier.
// A gap (no valid candidate: the wall time does not exist) gives the pre-transition offset, which lands one hour later on the
// wall clock.
function wallToUtcIso(dateStr, timeStr, tz) {
  const d = String(dateStr).split('-').map(Number);
  const t = String(timeStr || '00:00').split(':').map(Number);
  const naive = Date.UTC(d[0], (d[1] || 1) - 1, d[2] || 1, t[0] || 0, t[1] || 0, 0);
  const zone = tz || ztDeviceZone();
  const cands = [...new Set([
    naive - ztOffsetMs(naive - 86400000, zone),
    naive - ztOffsetMs(naive + 86400000, zone)
  ])];
  const valid = cands.filter(c => ztOffsetMs(c, zone) === naive - c);
  const pick = valid.length ? Math.min(...valid) : Math.max(...cands);
  return new Date(pick).toISOString();
}

// Event times (a check, a poll): the viewer's own zone, with no offset label.
function fmtEventTime(iso) {
  const ms = ztToMs(iso);
  if (ms === null) return '';
  return new Intl.DateTimeFormat([], { hour: '2-digit', minute: '2-digit', timeZone: ztDeviceZone() }).format(new Date(ms));
}
function fmtEventDateTime(iso) {
  const ms = ztToMs(iso);
  if (ms === null) return '';
  return new Intl.DateTimeFormat([], { dateStyle: 'short', timeStyle: 'medium', timeZone: ztDeviceZone() }).format(new Date(ms));
}
function fmtEventDate(iso) {
  const ms = ztToMs(iso);
  if (ms === null) return '';
  return new Intl.DateTimeFormat([], { day: 'numeric', month: 'short', timeZone: ztDeviceZone() }).format(new Date(ms));
}

// Whether the place's offset differs from the device's now (for a label next to an input).
function zoneDiffersFromDevice(tz) {
  if (!tz) return false;
  const now = Date.now();
  return ztOffsetMs(now, tz) !== ztOffsetMs(now, ztDeviceZone());
}
function zoneOffsetLabel(tz) {
  return ztOffsetLabel(Date.now(), tz || ztDeviceZone());
}

if (typeof module === 'object' && module && module.exports) {
  module.exports = {
    ztToMs, ztParts, ztOffsetMs, ztOffsetLabel, ztDeviceZone,
    fmtPlaceTime, fmtPlaceTimeLabelled, placeParts, placeNowWall, wallToUtcIso,
    fmtEventTime, fmtEventDateTime, fmtEventDate, zoneDiffersFromDevice, zoneOffsetLabel
  };
}
