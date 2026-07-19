#!/usr/bin/env node
// vendor.mjs — produce a single-profile copy of the UPM package for a game
// project that uses one authoring profile (classic/compact/ultra,
// docs/csharp-api.md). It copies unity/com.sqlitehost.runtime/ into an
// output directory, dropping the .cs files of the profiles you do NOT use.
//
// The three profiles are mutually independent and sit on the shared engine —
// nothing in the engine references a profile entry point — so dropping the
// unused ones leaves a package that still compiles. That invariant is pinned
// by tests/vendor-trim (each profile is trimmed and compiled in CI), so this
// stays honest as the runtime evolves. Fully reversible: re-copy the package.
//
// Usage:
//   node unity/vendor.mjs --profile <classic|compact|ultra> --out <dir> [--samples]
//
// Note: this trims by profile only. To also drop the optional validation,
// define SQLITEHOST_SLIM in the consuming project (Scripting Define Symbols);
// see docs/guides/vendored-footprint.md.

import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const unityDir = path.dirname(fileURLToPath(import.meta.url));
const defaultPackageDir = path.join(unityDir, "com.sqlitehost.runtime");

// Profile-specific runtime files (basenames under Runtime/Runtime/). Keep this
// in sync with the emitter profiles; tests/vendor-trim compiles each trim, so
// a missing or stale entry surfaces as a build failure, not silent breakage.
export const PROFILE_FILES = {
  classic: ["HostMethod.cs", "HostMethodSpecBuilder.cs", "ScalarFields.cs", "FieldsBuilders.cs"],
  compact: ["CompactHostMethod.cs"],
  ultra: ["UltraHostMethod.cs", "UltraFields.cs", "SqliteHostUltraValues.cs"],
};

export const PROFILES = Object.keys(PROFILE_FILES);

const EXCLUDED_DIRS = new Set(["bin", "obj"]);

/** Relative paths (under the package) of profiles OTHER than `profile`. */
export function excludedRelPaths(profile) {
  const set = new Set();
  for (const [name, files] of Object.entries(PROFILE_FILES)) {
    if (name === profile) continue;
    for (const file of files) {
      set.add(path.join("Runtime", "Runtime", file));
    }
  }
  return set;
}

function listFiles(dir, rel = "") {
  const out = [];
  for (const entry of fs
    .readdirSync(dir, { withFileTypes: true })
    .sort((a, b) => a.name.localeCompare(b.name))) {
    const entryRel = rel === "" ? entry.name : rel + "/" + entry.name;
    if (entry.isDirectory()) {
      if (!EXCLUDED_DIRS.has(entry.name)) out.push(...listFiles(path.join(dir, entry.name), entryRel));
    } else if (entry.isFile()) {
      out.push(entryRel);
    }
  }
  return out;
}

/**
 * Copy the package at `packageDir` into `outDir`, dropping the .cs files of
 * every profile except `profile` (and Samples~ unless includeSamples).
 * Returns { copied, skipped }.
 */
export function vendor({ profile, outDir, packageDir = defaultPackageDir, includeSamples = false }) {
  if (!PROFILES.includes(profile)) {
    throw new Error(`unknown profile "${profile}" (expected: ${PROFILES.join(", ")})`);
  }
  if (!fs.existsSync(packageDir)) {
    throw new Error(`package directory missing: ${packageDir}`);
  }
  const excluded = excludedRelPaths(profile);
  let copied = 0;
  let skipped = 0;
  for (const relPath of listFiles(packageDir)) {
    const normalized = relPath.split("/").join(path.sep);
    if (excluded.has(normalized)) {
      skipped++;
      continue;
    }
    if (!includeSamples && (relPath === "Samples~" || relPath.startsWith("Samples~/"))) {
      skipped++;
      continue;
    }
    const target = path.join(outDir, normalized);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(path.join(packageDir, normalized), target);
    copied++;
  }
  return { copied, skipped };
}

function isMain() {
  return process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
}

if (isMain()) {
  const args = process.argv.slice(2);
  let profile;
  let outDir;
  let includeSamples = false;
  for (let i = 0; i < args.length; i++) {
    if (args[i] === "--profile") profile = args[++i];
    else if (args[i] === "--out") outDir = args[++i];
    else if (args[i] === "--samples") includeSamples = true;
    else {
      console.error("unknown argument: " + args[i]);
      profile = undefined;
      break;
    }
  }
  if (!profile || !outDir || !PROFILES.includes(profile)) {
    console.error(
      "usage: node unity/vendor.mjs --profile <" + PROFILES.join("|") + "> --out <dir> [--samples]",
    );
    process.exit(2);
  }
  const { copied, skipped } = vendor({ profile, outDir, includeSamples });
  console.log(
    `vendored ${profile} profile → ${outDir}: ${copied} files copied, ${skipped} skipped ` +
      `(other profiles${includeSamples ? "" : " + samples"})`,
  );
}
