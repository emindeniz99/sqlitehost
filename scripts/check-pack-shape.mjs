#!/usr/bin/env node
/**
 * npm tarball shape check for the three publishable packages.
 *
 * `pnpm pack` each package and assert that what `package.json` promises is
 * actually inside the tarball: every `main`/`types`/`exports` target
 * resolves, `files` did not sweep in `src/` or the compiled tests, and the
 * TypeSpec library really ships its `.tsp` sources under `lib/`.
 *
 * This replaces the hand-run `pnpm pack && tar -tzf` checklist in
 * docs/guides/publishing.md §c. It works today even though all three
 * manifests still carry `"private": true` — that flag gates `publish`, not
 * `pack`, which is exactly why this can run per-PR while
 * scripts/check-npm-publishable.mjs cannot.
 *
 * Usage: node scripts/check-pack-shape.mjs   (packages must be built)
 */

import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, readdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");

const PACKAGES = [
  { dir: "typespec/library", mustContain: ["lib/main.tsp"] },
  { dir: "typescript/runtime-types", mustContain: [] },
  { dir: "typescript/authoring-sdk", mustContain: [] },
];

// Paths that must never reach a consumer: the TypeScript sources the
// `dist` build already replaced, and the compiled test tree that every
// manifest excludes with `!dist/test`.
const FORBIDDEN_PREFIXES = ["src/", "dist/test/"];

const errors = [];
const warnings = [];

/** Every file path promised by main/types/exports, relative to the package root. */
function declaredEntryPoints(manifest) {
  const out = new Set();
  for (const key of ["main", "types"]) {
    if (typeof manifest[key] === "string") out.add(manifest[key]);
  }
  const walk = (node) => {
    if (typeof node === "string") {
      // Conditional exports may name a directory or a wildcard; this repo
      // uses neither, so anything else is a mistake worth reporting.
      out.add(node);
    } else if (node && typeof node === "object") {
      for (const value of Object.values(node)) walk(value);
    }
  };
  walk(manifest.exports);
  return [...out].map((p) => p.replace(/^\.\//, ""));
}

for (const { dir, mustContain } of PACKAGES) {
  const pkgDir = join(ROOT, dir);
  const manifest = JSON.parse(readFileSync(join(pkgDir, "package.json"), "utf8"));
  const dest = mkdtempSync(join(tmpdir(), "sqlitehost-pack-"));
  let entries;
  try {
    execFileSync("pnpm", ["pack", "--pack-destination", dest], {
      cwd: pkgDir,
      stdio: ["ignore", "ignore", "inherit"],
    });
    const tgz = readdirSync(dest).find((f) => f.endsWith(".tgz"));
    if (!tgz) {
      errors.push(`${manifest.name}: pnpm pack produced no tarball`);
      continue;
    }
    // `package/` is npm's fixed root inside every tarball.
    entries = execFileSync("tar", ["-tzf", join(dest, tgz)], { encoding: "utf8" })
      .split("\n")
      .filter(Boolean)
      .map((p) => p.replace(/^package\//, ""));
  } finally {
    rmSync(dest, { recursive: true, force: true });
  }

  const has = (p) => entries.includes(p);

  for (const entry of declaredEntryPoints(manifest)) {
    if (!has(entry)) {
      errors.push(`${manifest.name}: package.json points at "${entry}", which is not in the tarball`);
    }
  }
  for (const extra of mustContain) {
    if (!has(extra)) errors.push(`${manifest.name}: "${extra}" is missing from the tarball`);
  }
  for (const prefix of FORBIDDEN_PREFIXES) {
    const leaked = entries.filter((p) => p.startsWith(prefix));
    if (leaked.length > 0) {
      errors.push(
        `${manifest.name}: ${leaked.length} file(s) under "${prefix}" leaked into the tarball ` +
          `(first: ${leaked[0]}) — check "files" in package.json`,
      );
    }
  }
  if (!has("LICENSE")) errors.push(`${manifest.name}: LICENSE is not in the tarball`);
  if (!has("README.md")) {
    // npm renders README.md as the package page. Missing one is a known
    // open item (docs/guides/publishing.md §c), not a reason to fail a PR.
    warnings.push(`${manifest.name}: no README.md — npm will show an empty package page`);
  }

  console.log(`${manifest.name}: ${entries.length} files, entry points resolve`);
}

for (const w of warnings) console.log(`::warning::${w}`);

if (errors.length > 0) {
  console.error("\nnpm tarball shape check FAILED:");
  for (const e of errors) console.error(`  - ${e}`);
  process.exit(1);
}
console.log(`\nAll ${PACKAGES.length} tarballs have the shape their manifests promise.`);
