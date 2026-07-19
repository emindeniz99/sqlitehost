#!/usr/bin/env node
// vendor.mjs — produce a slimmed copy of the UPM package for a game project
// that uses one authoring profile (classic/compact/ultra, docs/csharp-api.md).
// It copies unity/com.sqlitehost.runtime/ into an output directory, dropping
// the .cs files of the profiles you do NOT use, and (with --slim) the optional
// registration/binding validation as well.
//
// The three profiles are mutually independent and sit on the shared engine —
// nothing in the engine references a profile entry point — so dropping the
// unused ones leaves a package that still compiles. --slim additionally
// removes every `#if !SQLITEHOST_SLIM` block (the runtime's only conditional
// symbol) and the validation-only source files, producing a build with no
// registration/binding checks — matching the SQLITEHOST_SLIM compile define,
// but as plain source, so no Unity Scripting Define Symbol is needed. Both
// trims are pinned by tests/vendor-trim (each profile × mode is trimmed and
// compiled in CI), so this stays honest as the runtime evolves. Reversible:
// re-copy the package.
//
// Usage:
//   node unity/vendor.mjs --profile <classic|compact|ultra> --out <dir> [--slim] [--samples]
//
// --slim drops defense-in-depth (malformed-definition / binding checks). Only
// use it when scripts come exclusively from a backend already validated by the
// Java/TS validators (docs/validation.md) — see docs/guides/vendored-footprint.md.

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

// Validation-only source files (no `#if` guard of their own; referenced only
// from `#if !SQLITEHOST_SLIM` code). Under --slim their callers are stripped,
// so the files are dropped outright rather than left as dead code.
const SLIM_ONLY_FILES = [path.join("Runtime", "Runtime", "SqlParameterScanner.cs")];

const EXCLUDED_DIRS = new Set(["bin", "obj"]);
const SLIM_IF = /^#if\s+!SQLITEHOST_SLIM\b/;

/** Relative paths (under the package) of profiles OTHER than `profile`. */
export function excludedRelPaths(profile) {
  const set = new Set();
  for (const [name, files] of Object.entries(PROFILE_FILES)) {
    if (name === profile) continue;
    for (const file of files) set.add(path.join("Runtime", "Runtime", file));
  }
  return set;
}

/**
 * Remove every `#if !SQLITEHOST_SLIM … #endif` block — the guarded lines and
 * the directive lines. The runtime uses no other conditional-compilation
 * symbol and no `#else`/`#elif`, so a depth counter over `#if`/`#endif` is a
 * faithful stand-in for the C# preprocessor evaluating SQLITEHOST_SLIM=defined.
 */
export function stripSlim(text) {
  const out = [];
  let skip = 0;
  for (const line of text.split("\n")) {
    const t = line.trim();
    if (skip > 0) {
      if (t.startsWith("#if")) skip++;
      else if (t.startsWith("#endif")) skip--;
      continue;
    }
    if (SLIM_IF.test(t)) {
      skip = 1;
      continue;
    }
    out.push(line);
  }
  return out.join("\n");
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
 * every profile except `profile` (and Samples~ unless includeSamples). With
 * `slim`, also drop the validation-only files and strip `#if !SQLITEHOST_SLIM`
 * blocks from every copied .cs. Returns { copied, skipped }.
 */
export function vendor({
  profile,
  outDir,
  packageDir = defaultPackageDir,
  includeSamples = false,
  slim = false,
}) {
  if (!PROFILES.includes(profile)) {
    throw new Error(`unknown profile "${profile}" (expected: ${PROFILES.join(", ")})`);
  }
  if (!fs.existsSync(packageDir)) {
    throw new Error(`package directory missing: ${packageDir}`);
  }
  const excluded = excludedRelPaths(profile);
  if (slim) for (const f of SLIM_ONLY_FILES) excluded.add(f);
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
    const source = path.join(packageDir, normalized);
    const target = path.join(outDir, normalized);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    if (slim && relPath.endsWith(".cs")) {
      fs.writeFileSync(target, stripSlim(fs.readFileSync(source, "utf8")));
    } else {
      fs.copyFileSync(source, target);
    }
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
  let slim = false;
  let ok = true;
  for (let i = 0; i < args.length; i++) {
    if (args[i] === "--profile") profile = args[++i];
    else if (args[i] === "--out") outDir = args[++i];
    else if (args[i] === "--samples") includeSamples = true;
    else if (args[i] === "--slim") slim = true;
    else {
      console.error("unknown argument: " + args[i]);
      ok = false;
      break;
    }
  }
  if (!ok || !profile || !outDir || !PROFILES.includes(profile)) {
    console.error(
      "usage: node unity/vendor.mjs --profile <" +
        PROFILES.join("|") +
        "> --out <dir> [--slim] [--samples]",
    );
    process.exit(2);
  }
  const { copied, skipped } = vendor({ profile, outDir, includeSamples, slim });
  const dropped = ["other profiles", slim ? "validation" : null, includeSamples ? null : "samples"]
    .filter(Boolean)
    .join(" + ");
  console.log(
    `vendored ${profile}${slim ? " (slim)" : ""} → ${outDir}: ` +
      `${copied} files copied, ${skipped} skipped (${dropped})`,
  );
}
