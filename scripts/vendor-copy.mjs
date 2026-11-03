// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copies the browser files of a packed @ballast JavaScript package into an app's wwwroot, and checks that the copies are
// still byte-identical to the package.
//
// The apps commit these copies, because their Content-Security-Policy never allows a CDN and their deploy builds a clean
// checkout with no npm step. The copy reads the tarball that `npm pack` wrote, never the source tree, so a copy always matches
// a version that was packed. Each copy writes a manifest (ballast-vendor.json) beside the files, which names the package, its
// version and the SHA-256 of each file; the check reads the manifest and the tarball.
//
// Usage:
//   node scripts/vendor-copy.mjs copy  --package artifacts/packages/ballast-web-0.1.0-preview.2.tgz --app <wwwroot> [--dest <dir>] [--map <file>=<target>]...
//   node scripts/vendor-copy.mjs check --package artifacts/packages/ballast-web-0.1.0-preview.2.tgz --app <wwwroot> [--dest <dir>]
//
// --dest is relative to --app and defaults to vendor/ballast/<name>, where <name> is the package name without its scope
// (@ballast/web gives vendor/ballast/web). --map sends one browser file to a different target, relative to --app, instead of
// the destination folder: GetThere's zone-time.js goes to map/zone-time.js, which its offline map bundle reads.
// Exit codes: 0 ok, 1 a copy differs or is missing, 2 a usage error or an unreadable package.

import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import zlib from 'node:zlib';

export const MANIFEST_NAME = 'ballast-vendor.json';

const BLOCK = 512;

function tarField(block, start, length) {
  const end = block.indexOf(0, start);
  const stop = end === -1 || end > start + length ? start + length : end;
  return block.toString('utf8', start, stop);
}

function paxPath(data) {
  let pos = 0;
  let found = null;
  while (pos < data.length) {
    const space = data.indexOf(0x20, pos);
    if (space === -1) break;
    const length = Number.parseInt(data.toString('ascii', pos, space), 10);
    if (!Number.isFinite(length) || length <= 0) throw new Error('A pax header record has no length.');
    const record = data.toString('utf8', space + 1, pos + length - 1);
    const eq = record.indexOf('=');
    if (eq > 0 && record.slice(0, eq) === 'path') found = record.slice(eq + 1);
    pos += length;
  }
  return found;
}

/**
 * Reads a gzipped tarball into a map of its regular files. Names have no leading "package/" folder, so a file is "browser/html.js".
 * @param {string} tarballPath the .tgz file written by npm pack
 * @returns {Map<string, Buffer>}
 * @throws {Error} the file is not a gzipped tar, or its headers are malformed.
 */
export function readPackage(tarballPath) {
  const tar = zlib.gunzipSync(fs.readFileSync(tarballPath));
  const files = new Map();
  let offset = 0;
  let longName = null;
  let extendedPath = null;
  while (offset + BLOCK <= tar.length) {
    const header = tar.subarray(offset, offset + BLOCK);
    if (header.every((byte) => byte === 0)) break;

    const sizeText = tarField(header, 124, 12).trim();
    const size = Number.parseInt(sizeText || '0', 8);
    if (!Number.isFinite(size) || size < 0) throw new Error(`A tar header in ${tarballPath} has a bad size "${sizeText}".`);

    const flag = header[156] === 0 ? '0' : String.fromCharCode(header[156]);
    const dataStart = offset + BLOCK;
    const data = tar.subarray(dataStart, dataStart + size);
    offset = dataStart + Math.ceil(size / BLOCK) * BLOCK;

    if (flag === 'x') {
      extendedPath = paxPath(data) ?? extendedPath;
      continue;
    }
    if (flag === 'L') {
      longName = data.toString('utf8').replace(/\0+$/, '');
      continue;
    }
    if (flag === 'g') continue;

    const magic = tarField(header, 257, 6);
    const prefix = magic.startsWith('ustar') ? tarField(header, 345, 155) : '';
    let name = prefix ? `${prefix}/${tarField(header, 0, 100)}` : tarField(header, 0, 100);
    if (longName !== null) name = longName;
    if (extendedPath !== null) name = extendedPath;
    longName = null;
    extendedPath = null;

    if (flag !== '0') continue;
    files.set(name.replace(/^\.?\/?package\//, ''), Buffer.from(data));
  }
  return files;
}

function packageIdentity(files, tarballPath) {
  const manifest = files.get('package.json');
  if (!manifest) throw new Error(`${tarballPath} has no package.json.`);
  const parsed = JSON.parse(manifest.toString('utf8'));
  if (typeof parsed.name !== 'string' || typeof parsed.version !== 'string') {
    throw new Error(`${tarballPath}: package.json needs a name and a version.`);
  }
  return { name: parsed.name, version: parsed.version };
}

function browserFiles(files) {
  return [...files.keys()]
    .filter((name) => name.startsWith('browser/'))
    .sort()
    .map((name) => ({ source: name, relative: name.slice('browser/'.length), bytes: files.get(name) }));
}

function shortName(name) {
  return name.split('/').pop();
}

function defaultDest(identity) {
  return `vendor/ballast/${shortName(identity.name)}`;
}

function sha256(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}

function insideApp(appRoot, target) {
  const root = path.resolve(appRoot);
  const full = path.resolve(root, target);
  if (full !== root && !full.startsWith(root + path.sep)) {
    throw new Error(`The target "${target}" is outside the app folder.`);
  }
  return full;
}

function readManifest(manifestPath) {
  if (!fs.existsSync(manifestPath)) return null;
  return JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
}

/**
 * Copies the browser files of a package into an app, writes the manifest, and removes the copies the previous manifest held that
 * the package no longer has.
 * @param {{tarball: string, appRoot: string, dest?: string, map?: Record<string, string>}} options
 * @returns {{package: string, version: string, files: {source: string, target: string, sha256: string}[]}}
 * @throws {Error} the package is unreadable, a target is outside the app, or a map names a file the package does not have.
 */
export function copyPackage({ tarball, appRoot, dest, map = {} }) {
  const files = readPackage(tarball);
  const identity = packageIdentity(files, tarball);
  const destDir = (dest ?? defaultDest(identity)).replace(/\\/g, '/').replace(/\/+$/, '');
  const browser = browserFiles(files);
  if (browser.length === 0) throw new Error(`${tarball} has no browser/ files to copy.`);

  for (const name of Object.keys(map)) {
    if (!browser.some((file) => file.relative === name)) {
      throw new Error(`The map names "${name}", which the package does not have in browser/.`);
    }
  }

  const entries = browser.map((file) => {
    const target = (map[file.relative] ?? `${destDir}/${file.relative}`).replace(/\\/g, '/');
    const full = insideApp(appRoot, target);
    fs.mkdirSync(path.dirname(full), { recursive: true });
    fs.writeFileSync(full, file.bytes);
    return { source: file.source, target, sha256: sha256(file.bytes) };
  });

  const manifestPath = insideApp(appRoot, `${destDir}/${MANIFEST_NAME}`);
  const previous = readManifest(manifestPath);
  const kept = new Set(entries.map((entry) => entry.target));
  for (const old of previous?.files ?? []) {
    if (!kept.has(old.target)) fs.rmSync(insideApp(appRoot, old.target), { force: true });
  }

  const manifest = { package: identity.name, version: identity.version, files: entries };
  fs.mkdirSync(path.dirname(manifestPath), { recursive: true });
  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  return manifest;
}

/**
 * Checks that every copy the manifest names is byte-identical to its file in the package, and that the package has no browser file
 * the copy lacks. It never writes.
 * @param {{tarball: string, appRoot: string, dest?: string}} options
 * @returns {{ok: boolean, problems: string[]}} ok is true when no problem was found.
 */
export function checkPackage({ tarball, appRoot, dest }) {
  const files = readPackage(tarball);
  const identity = packageIdentity(files, tarball);
  const destDir = (dest ?? defaultDest(identity)).replace(/\\/g, '/').replace(/\/+$/, '');
  const manifestPath = insideApp(appRoot, `${destDir}/${MANIFEST_NAME}`);
  const manifest = readManifest(manifestPath);
  if (!manifest) return { ok: false, problems: [`No manifest at ${destDir}/${MANIFEST_NAME}. Run the copy first.`] };

  const problems = [];
  if (manifest.package !== identity.name || manifest.version !== identity.version) {
    problems.push(`The copy is ${manifest.package} ${manifest.version}; the package is ${identity.name} ${identity.version}.`);
  }

  const listed = new Set();
  for (const entry of manifest.files ?? []) {
    listed.add(entry.source);
    const packaged = files.get(entry.source);
    if (!packaged) {
      problems.push(`${entry.source} is in the manifest but not in the package.`);
      continue;
    }
    const full = insideApp(appRoot, entry.target);
    if (!fs.existsSync(full)) {
      problems.push(`${entry.target} is missing.`);
      continue;
    }
    if (!fs.readFileSync(full).equals(packaged)) {
      problems.push(`${entry.target} differs from ${entry.source} in the package.`);
    }
  }

  for (const file of browserFiles(files)) {
    if (!listed.has(file.source)) problems.push(`${file.source} is in the package but not in the copy.`);
  }

  return { ok: problems.length === 0, problems };
}

const VALUED_FLAGS = new Set(['--package', '--app', '--dest', '--map']);

function parseArgs(argv) {
  const [command, ...rest] = argv;
  const options = { command, map: {} };
  for (let i = 0; i < rest.length; i++) {
    const flag = rest[i];
    if (!VALUED_FLAGS.has(flag)) throw new Error(`Unknown argument "${flag}".`);
    const value = rest[i + 1];
    if (value === undefined) throw new Error(`${flag} needs a value.`);
    i++;

    if (flag === '--package') options.tarball = value;
    else if (flag === '--app') options.appRoot = value;
    else if (flag === '--dest') options.dest = value;
    else {
      const eq = value.indexOf('=');
      if (eq <= 0 || eq === value.length - 1) throw new Error('--map needs file=target.');
      options.map[value.slice(0, eq)] = value.slice(eq + 1);
    }
  }
  return options;
}

function main(argv) {
  let options;
  try {
    options = parseArgs(argv);
  } catch (error) {
    console.error(error.message);
    return 2;
  }
  if (!options.tarball || !options.appRoot || !['copy', 'check'].includes(options.command)) {
    console.error('Usage: vendor-copy.mjs copy|check --package <file.tgz> --app <wwwroot> [--dest <dir>] [--map <file>=<target>]...');
    return 2;
  }

  try {
    if (options.command === 'copy') {
      const manifest = copyPackage(options);
      console.log(`Copied ${manifest.package} ${manifest.version}: ${manifest.files.length} file(s).`);
      return 0;
    }
    const result = checkPackage(options);
    if (result.ok) {
      console.log('The copies match the package.');
      return 0;
    }
    for (const problem of result.problems) console.error(problem);
    return 1;
  } catch (error) {
    console.error(error.message);
    return 2;
  }
}

const invokedDirectly = process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (invokedDirectly) {
  process.exitCode = main(process.argv.slice(2));
}

export { main as runCli };
