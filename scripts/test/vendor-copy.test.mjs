// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The vendor copy: it reads a packed package, copies its browser files, writes a manifest, and checks the copies byte by byte.
// Packages are built here as real gzipped tarballs in a temporary folder, and one case packs the @ballast/web package itself.

import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import zlib from 'node:zlib';
import { after, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { checkPackage, copyPackage, MANIFEST_NAME, readPackage, runCli } from '../vendor-copy.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB_PACKAGE = path.resolve(HERE, '..', '..', 'js', 'web');

const workRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'ballast-vendor-'));
after(() => fs.rmSync(workRoot, { recursive: true, force: true }));
let counter = 0;
function freshDir(label) {
  counter += 1;
  const dir = path.join(workRoot, `${label}-${counter}`);
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

// A ustar header for one entry, with a valid checksum.
function header(name, size, flag = '0', prefix = '') {
  const h = Buffer.alloc(512);
  h.write(name, 0, 100, 'utf8');
  h.write('0000644\0', 100, 'ascii');
  h.write('0000000\0', 108, 'ascii');
  h.write('0000000\0', 116, 'ascii');
  h.write(size.toString(8).padStart(11, '0') + '\0', 124, 'ascii');
  h.write('00000000000\0', 136, 'ascii');
  h.write('        ', 148, 'ascii');
  h.write(flag, 156, 'ascii');
  h.write('ustar\0', 257, 'ascii');
  h.write('00', 263, 'ascii');
  h.write(prefix, 345, 155, 'utf8');
  let sum = 0;
  for (const byte of h) sum += byte;
  h.write(sum.toString(8).padStart(6, '0') + '\0 ', 148, 'ascii');
  return h;
}

function paddedData(content) {
  const pad = (512 - (content.length % 512)) % 512;
  return Buffer.concat([content, Buffer.alloc(pad)]);
}

// A pax record for one key: its length counts its own digits, so the length is found by repeating until it is stable.
function paxRecord(key, value) {
  const rest = ` ${key}=${value}\n`;
  let length = Buffer.byteLength(rest) + 1;
  while (String(length).length + Buffer.byteLength(rest) !== length) {
    length = String(length).length + Buffer.byteLength(rest);
  }
  return Buffer.from(`${length}${rest}`, 'utf8');
}

// Builds a .tgz. An entry with a long name gets a pax header that names it in full.
function makeTarball(entries, fileName) {
  const blocks = [];
  for (const entry of entries) {
    if (entry.name.length > 100) {
      const record = paxRecord('path', entry.name);
      blocks.push(header('PaxHeader', record.length, 'x'), paddedData(record));
    }
    const content = Buffer.from(entry.content, 'utf8');
    blocks.push(header(entry.name.length > 100 ? 'long' : entry.name, content.length), paddedData(content));
  }
  blocks.push(Buffer.alloc(1024));
  const file = path.join(freshDir('tgz'), fileName);
  fs.writeFileSync(file, zlib.gzipSync(Buffer.concat(blocks)));
  return file;
}

function packageJson(name, version) {
  return JSON.stringify({ name, version });
}

function webTarball(version, extraFiles = {}) {
  const entries = [
    { name: 'package/package.json', content: packageJson('@ballast/web', version) },
    { name: 'package/README.md', content: '# web\n' },
    { name: 'package/browser/html.js', content: '// html\n' },
    { name: 'package/browser/utc.js', content: '// utc\n' },
    { name: 'package/browser/zone-time.js', content: '// zone\n' }
  ];
  for (const [name, content] of Object.entries(extraFiles)) entries.push({ name: `package/browser/${name}`, content });
  return makeTarball(entries, `ballast-web-${version}.tgz`);
}

describe('readPackage', () => {
  it('reads the regular files, without the package folder, and follows a pax path for a long name', () => {
    const longName = 'package/browser/' + 'deep/'.repeat(12) + 'a-file-with-a-long-name.js';
    const tgz = makeTarball([
      { name: 'package/package.json', content: packageJson('@ballast/web', '0.1.0-preview.1') },
      { name: longName, content: 'long' }
    ], 'read.tgz');

    const files = readPackage(tgz);

    assert.equal(files.get('package.json').toString('utf8'), packageJson('@ballast/web', '0.1.0-preview.1'));
    assert.equal(files.get(longName.replace('package/', '')).toString('utf8'), 'long');
  });

  it('refuses a file that is not a gzipped tar', () => {
    const file = path.join(freshDir('bad'), 'bad.tgz');
    fs.writeFileSync(file, 'not an archive');
    assert.throws(() => readPackage(file));
  });
});

describe('copyPackage', () => {
  it('copies every browser file into vendor/ballast/<name> and writes a manifest', () => {
    const tgz = webTarball('0.1.0-preview.1');
    const app = freshDir('app');

    const manifest = copyPackage({ tarball: tgz, appRoot: app });

    assert.equal(manifest.package, '@ballast/web');
    assert.equal(manifest.version, '0.1.0-preview.1');
    assert.deepEqual(manifest.files.map((f) => f.target), [
      'vendor/ballast/web/html.js',
      'vendor/ballast/web/utc.js',
      'vendor/ballast/web/zone-time.js'
    ]);
    assert.equal(fs.readFileSync(path.join(app, 'vendor', 'ballast', 'web', 'html.js'), 'utf8'), '// html\n');
    const written = JSON.parse(fs.readFileSync(path.join(app, 'vendor', 'ballast', 'web', MANIFEST_NAME), 'utf8'));
    assert.equal(written.files[0].sha256.length, 64);
  });

  it('sends a mapped file to its own target and keeps it out of the destination folder', () => {
    const tgz = webTarball('0.1.0-preview.1');
    const app = freshDir('app-map');

    copyPackage({ tarball: tgz, appRoot: app, map: { 'zone-time.js': 'map/zone-time.js' } });

    assert.equal(fs.readFileSync(path.join(app, 'map', 'zone-time.js'), 'utf8'), '// zone\n');
    assert.equal(fs.existsSync(path.join(app, 'vendor', 'ballast', 'web', 'zone-time.js')), false);
    assert.equal(fs.existsSync(path.join(app, 'vendor', 'ballast', 'web', 'html.js')), true);
  });

  it('removes the copies the previous manifest held that the new package no longer has', () => {
    const app = freshDir('app-stale');
    copyPackage({ tarball: webTarball('0.1.0-preview.1'), appRoot: app });
    const second = makeTarball([
      { name: 'package/package.json', content: packageJson('@ballast/web', '0.1.0-preview.2') },
      { name: 'package/browser/html.js', content: '// html 2\n' }
    ], 'second.tgz');

    copyPackage({ tarball: second, appRoot: app });

    assert.equal(fs.existsSync(path.join(app, 'vendor', 'ballast', 'web', 'utc.js')), false);
    assert.equal(fs.readFileSync(path.join(app, 'vendor', 'ballast', 'web', 'html.js'), 'utf8'), '// html 2\n');
  });

  it('refuses a target outside the app folder', () => {
    const app = freshDir('app-escape');
    assert.throws(() => copyPackage({ tarball: webTarball('0.1.0-preview.1'), appRoot: app, map: { 'html.js': '../escape.js' } }),
      /outside the app folder/);
    assert.equal(fs.existsSync(path.join(path.dirname(app), 'escape.js')), false);
  });

  it('refuses a map entry the package does not have', () => {
    const app = freshDir('app-missing-map');
    assert.throws(() => copyPackage({ tarball: webTarball('0.1.0-preview.1'), appRoot: app, map: { 'nope.js': 'map/nope.js' } }),
      /does not have/);
  });

  it('refuses a package with no browser files', () => {
    const tgz = makeTarball([{ name: 'package/package.json', content: packageJson('@ballast/x', '1.0.0') }], 'nobrowser.tgz');
    assert.throws(() => copyPackage({ tarball: tgz, appRoot: freshDir('app-nobrowser') }), /no browser/);
  });
});

describe('checkPackage', () => {
  it('passes straight after a copy', () => {
    const tgz = webTarball('0.1.0-preview.1');
    const app = freshDir('check-ok');
    copyPackage({ tarball: tgz, appRoot: app, map: { 'zone-time.js': 'map/zone-time.js' } });

    const result = checkPackage({ tarball: tgz, appRoot: app, map: undefined });

    assert.deepEqual(result, { ok: true, problems: [] });
  });

  it('reports a copy that was edited by hand', () => {
    const tgz = webTarball('0.1.0-preview.1');
    const app = freshDir('check-edited');
    copyPackage({ tarball: tgz, appRoot: app });
    fs.appendFileSync(path.join(app, 'vendor', 'ballast', 'web', 'html.js'), '// edited\n');

    const result = checkPackage({ tarball: tgz, appRoot: app });

    assert.equal(result.ok, false);
    assert.deepEqual(result.problems, ['vendor/ballast/web/html.js differs from browser/html.js in the package.']);
  });

  it('reports a copy that is missing', () => {
    const tgz = webTarball('0.1.0-preview.1');
    const app = freshDir('check-missing');
    copyPackage({ tarball: tgz, appRoot: app });
    fs.rmSync(path.join(app, 'vendor', 'ballast', 'web', 'utc.js'));

    const result = checkPackage({ tarball: tgz, appRoot: app });

    assert.deepEqual(result.problems, ['vendor/ballast/web/utc.js is missing.']);
  });

  it('reports a package file the copy does not have, and a copy of another version', () => {
    const app = freshDir('check-version');
    copyPackage({ tarball: webTarball('0.1.0-preview.1'), appRoot: app });
    const newer = webTarball('0.1.0-preview.2', { 'extra.js': '// extra\n' });

    const result = checkPackage({ tarball: newer, appRoot: app });

    assert.equal(result.ok, false);
    assert.ok(result.problems.includes('The copy is @ballast/web 0.1.0-preview.1; the package is @ballast/web 0.1.0-preview.2.'));
    assert.ok(result.problems.includes('browser/extra.js is in the package but not in the copy.'));
  });

  it('says so when no copy has been made', () => {
    const result = checkPackage({ tarball: webTarball('0.1.0-preview.1'), appRoot: freshDir('check-none') });

    assert.equal(result.ok, false);
    assert.match(result.problems[0], /No manifest/);
  });
});

describe('the command line', () => {
  it('returns 2 for a usage error, 0 for a copy, and 1 for a check that fails', () => {
    const tgz = webTarball('0.1.0-preview.1');
    const app = freshDir('cli');
    const originalLog = console.log;
    const originalError = console.error;
    console.log = () => {};
    console.error = () => {};
    try {
      assert.equal(runCli(['copy']), 2);
      assert.equal(runCli(['copy', '--package', tgz, '--app', app]), 0);
      assert.equal(runCli(['check', '--package', tgz, '--app', app]), 0);
      fs.rmSync(path.join(app, 'vendor', 'ballast', 'web', 'html.js'));
      assert.equal(runCli(['check', '--package', tgz, '--app', app]), 1);
      assert.equal(runCli(['copy', '--package', tgz, '--app', app, '--map', 'no-equals']), 2);
      assert.equal(runCli(['copy', '--package', tgz, '--app', app, '--bogus', 'x']), 2);
    } finally {
      console.log = originalLog;
      console.error = originalError;
    }
  });
});

// Runs npm. Under `npm test` the npm script is known, so it runs with node and no shell; otherwise npm is a .cmd file on Windows and
// needs the shell.
function runNpm(args, cwd) {
  const cli = process.env.npm_execpath;
  if (cli && cli.endsWith('.js')) return spawnSync(process.execPath, [cli, ...args], { cwd, encoding: 'utf8' });
  return spawnSync(`npm ${args.map((arg) => `"${arg}"`).join(' ')}`, { cwd, encoding: 'utf8', shell: true });
}

describe('the real package', () => {
  it('packs @ballast/web with npm, copies it, and the copy checks out', () => {
    const packDir = freshDir('packed');
    const result = runNpm(['pack', '--pack-destination', packDir], WEB_PACKAGE);
    assert.equal(result.status, 0, result.stderr);
    const tgz = path.join(packDir, 'ballast-web-0.1.0-preview.1.tgz');
    assert.ok(fs.existsSync(tgz), `npm pack wrote ${tgz}`);

    const app = freshDir('real-app');
    const manifest = copyPackage({ tarball: tgz, appRoot: app });

    assert.equal(manifest.package, '@ballast/web');
    assert.ok(manifest.files.some((f) => f.target === 'vendor/ballast/web/html.js'));
    assert.deepEqual(checkPackage({ tarball: tgz, appRoot: app }), { ok: true, problems: [] });
  });
});
