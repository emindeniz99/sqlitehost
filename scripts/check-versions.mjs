#!/usr/bin/env node
// Asserts that every versioned manifest in the repo carries the same
// version as version.txt — and, when a tag is passed, that the tag agrees
// with it too.
//
// Why this exists: release-please keeps ~20 manifests in lockstep through
// the extra-files list in release-please-config.json. If one of those
// entries stops matching (a file moves, an updater's xpath no longer
// selects anything), release-please does NOT fail — it silently leaves
// that file behind, and the next release publishes a stale version to one
// registry only. This script reads the same config and checks the result,
// so the mismatch is loud.
//
// Usage:
//   node scripts/check-versions.mjs            # everything matches version.txt
//   node scripts/check-versions.mjs v1.2.3     # ...and matches the tag
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = (rel) => readFileSync(join(root, rel), 'utf8');

const expected = read('version.txt').trim();
const problems = [];

// The Maven release version is the parent POM's <version> in the parent
// itself and the <parent><version> in each module — both sit directly
// after the sqlite-host-parent artifactId, so one anchored match covers
// every POM and never catches a plugin or dependency version.
const POM_VERSION = /<artifactId>sqlite-host-parent<\/artifactId>\s*<version>([^<]+)<\/version>/;

function versionOf(path, type) {
  const src = read(path);
  if (type === 'json') return JSON.parse(src).version;
  if (type === 'pom') return POM_VERSION.exec(src)?.[1];
  if (type === 'xml') return /<Version>([^<]+)<\/Version>/.exec(src)?.[1];
  throw new Error(`unhandled extra-file type: ${type}`);
}

const config = JSON.parse(read('release-please-config.json'));
const extraFiles = config.packages['.']['extra-files'];
if (!extraFiles?.length) problems.push('release-please-config.json lists no extra-files');

for (const entry of extraFiles ?? []) {
  let actual;
  try {
    actual = versionOf(entry.path, entry.type);
  } catch (err) {
    problems.push(`${entry.path}: cannot read version (${err.message})`);
    continue;
  }
  if (actual === undefined) {
    problems.push(`${entry.path}: no version found — this file's updater is probably stale`);
  } else if (actual !== expected) {
    problems.push(`${entry.path}: ${actual} !== ${expected} (version.txt)`);
  }
}

const tag = process.argv[2];
if (tag) {
  const tagVersion = tag.replace(/^v/, '');
  if (tagVersion !== expected) {
    problems.push(`tag ${tag} does not match version.txt (${expected})`);
  }
}

if (problems.length) {
  for (const p of problems) console.error(`::error::${p}`);
  console.error(`\n${problems.length} version mismatch(es). Every manifest must carry ${expected}.`);
  console.error('Fix the release-please extra-files entry that stopped matching, then re-cut the release.');
  process.exit(1);
}

console.log(`all ${extraFiles.length} manifests + version.txt agree on ${expected}${tag ? ` (tag ${tag})` : ''}`);
