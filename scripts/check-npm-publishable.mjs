#!/usr/bin/env node
// Guards the npm publish step. Two failures it must never let through:
//
//  1. `"private": true` — the three publishable packages carry it as a
//     DELIBERATE publish gate (docs/guides/publishing.md §c). pnpm refuses
//     to publish a private package, so nothing can ship by accident; the
//     flag is removed once by hand on registry-bootstrap day. Until then
//     this script says so in plain words instead of leaving a cryptic
//     pnpm error in the log.
//
//  2. A published package depending on an UNPUBLISHED workspace package.
//     `pnpm publish` rewrites `workspace:*` to a real version range in the
//     tarball, so the dependency looks ordinary — and every `npm install`
//     of it fails with 404 because that package was never published.
//
// Usage: node scripts/check-npm-publishable.mjs
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

// The packages release.yml publishes, in dependency order.
const PUBLISHABLE = [
  { name: '@sqlite-host/runtime-types', dir: 'typescript/runtime-types' },
  { name: '@sqlite-host/authoring', dir: 'typescript/authoring-sdk' },
  { name: '@sqlite-host/typespec', dir: 'typespec/library' },
];
const publishableNames = new Set(PUBLISHABLE.map((p) => p.name));

const problems = [];

for (const pkg of PUBLISHABLE) {
  const manifestPath = join(pkg.dir, 'package.json');
  const manifest = JSON.parse(readFileSync(join(root, manifestPath), 'utf8'));

  if (manifest.name !== pkg.name) {
    problems.push(`${manifestPath}: name is ${manifest.name}, expected ${pkg.name}`);
  }

  if (manifest.private === true) {
    problems.push(
      `${manifestPath} still has "private": true — the deliberate publish gate.\n` +
        '    Registry-bootstrap day: create the @sqlite-host org on npmjs.com, then remove\n' +
        '    "private": true from all three publishable manifests in one commit. Until that\n' +
        '    commit lands, npm publishing is meant to fail here.'
    );
  }

  for (const field of ['dependencies', 'peerDependencies', 'optionalDependencies']) {
    for (const [dep, spec] of Object.entries(manifest[field] ?? {})) {
      if (typeof spec === 'string' && spec.startsWith('workspace:') && !publishableNames.has(dep)) {
        problems.push(
          `${manifestPath}: ${field}.${dep} = "${spec}" but ${dep} is never published.\n` +
            `    pnpm rewrites the workspace spec to a real range in the tarball, so every\n` +
            `    consumer install of ${manifest.name} would 404 on ${dep}.\n` +
            `    Fix before the first publish: publish ${dep} too, bundle its code, or drop the dependency.`
        );
      }
    }
  }
}

if (problems.length) {
  for (const p of problems) console.error(`::error::${p}`);
  console.error(`\n${problems.length} blocker(s) — npm publish refused.`);
  process.exit(1);
}

console.log(`${PUBLISHABLE.length} packages are publishable`);
