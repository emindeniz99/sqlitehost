#!/usr/bin/env node
// sync.mjs — mirrors the C# sources from csharp/ into the UPM package
// unity/com.sqlitehost.runtime/. The csharp/ projects are the single
// source of truth; the copies here exist only so Unity can consume the
// package without reaching outside its folder. Re-runnable at any time.
//
// Modes:
//   node unity/sync.mjs           regenerate the synced copies (deterministic)
//   node unity/sync.mjs --check   exit 1 with a diff listing when copies drift
//
// What is synced (source of truth -> synced copy):
//   csharp/SqliteHost.Abstractions/**/*.cs       -> com.sqlitehost.runtime/Runtime/Abstractions/
//   csharp/SqliteHost.Runtime/**/*.cs            -> com.sqlitehost.runtime/Runtime/Runtime/
//   csharp/SqliteHost.Generated.Sample/**/*.g.cs -> com.sqlitehost.runtime/Samples~/GeneratedSample/
//   bin/ and obj/ directories are always excluded.
//
// Transform applied during sync — InternalsVisibleTo strip:
//   In the UPM package, Abstractions and Runtime compile into ONE assembly
//   (Runtime/SqliteHost.asmdef), so [assembly: InternalsVisibleTo("SqliteHost.Runtime")]
//   would be a redundant self-reference (internals are already visible
//   inside a single assembly), and InternalsVisibleTo("SqliteHost.Tests")
//   points at a test assembly that is never shipped to Unity. The strip
//   rule is deliberately generic: ANY line consisting of an
//   `[assembly: InternalsVisibleTo(...)]` attribute is dropped; the rest of
//   the file (including its structure) is copied byte-for-byte. Today the
//   csharp/ projects declare InternalsVisibleTo in their .csproj files, so
//   the strip is a safety net for the day it moves into a .cs file.
//
// Ownership: each sync set only owns files matching its pattern inside its
// target folder. Handwritten package files (package.json, SqliteHost.asmdef,
// SmokeBehaviour.cs, SqliteHost.Sample.asmdef) and any *.meta files Unity
// generates are never written or deleted by this script.

import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const unityDir = path.dirname(fileURLToPath(import.meta.url));
const csharpDir = path.join(path.dirname(unityDir), "csharp");
const packageDir = path.join(unityDir, "com.sqlitehost.runtime");

const EXCLUDED_DIRS = new Set(["bin", "obj"]);
const INTERNALS_VISIBLE_TO_LINE =
  /^\s*\[\s*assembly\s*:\s*(?:System\.Runtime\.CompilerServices\.)?InternalsVisibleTo\s*\(.*\)\s*\]\s*\r?$/;

const SYNC_SETS = [
  {
    name: "Abstractions",
    sourceDir: path.join(csharpDir, "SqliteHost.Abstractions"),
    targetDir: path.join(packageDir, "Runtime", "Abstractions"),
    owns: (relPath) => relPath.endsWith(".cs"),
  },
  {
    name: "Runtime",
    sourceDir: path.join(csharpDir, "SqliteHost.Runtime"),
    targetDir: path.join(packageDir, "Runtime", "Runtime"),
    owns: (relPath) => relPath.endsWith(".cs"),
  },
  {
    name: "GeneratedSample",
    sourceDir: path.join(csharpDir, "SqliteHost.Generated.Sample"),
    targetDir: path.join(packageDir, "Samples~", "GeneratedSample"),
    owns: (relPath) => relPath.endsWith(".g.cs"),
  },
];

function listFiles(dir, rel = "") {
  if (!fs.existsSync(dir)) {
    return [];
  }
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) =>
    a.name.localeCompare(b.name)
  )) {
    const entryRel = rel === "" ? entry.name : rel + "/" + entry.name;
    if (entry.isDirectory()) {
      if (!EXCLUDED_DIRS.has(entry.name)) {
        out.push(...listFiles(path.join(dir, entry.name), entryRel));
      }
    } else if (entry.isFile()) {
      out.push(entryRel);
    }
  }
  return out;
}

function transform(text) {
  return text
    .split("\n")
    .filter((line) => !INTERNALS_VISIBLE_TO_LINE.test(line))
    .join("\n");
}

// relPath -> expected content, for the files a set owns.
function expectedFiles(set) {
  const expected = new Map();
  for (const relPath of listFiles(set.sourceDir)) {
    if (set.owns(relPath)) {
      expected.set(
        relPath,
        transform(fs.readFileSync(path.join(set.sourceDir, relPath), "utf8"))
      );
    }
  }
  return expected;
}

function ownedTargetFiles(set) {
  return listFiles(set.targetDir).filter((relPath) => set.owns(relPath));
}

function firstDifference(expected, actual) {
  const e = expected.split("\n");
  const a = actual.split("\n");
  const n = Math.max(e.length, a.length);
  for (let i = 0; i < n; i++) {
    if (e[i] !== a[i]) {
      return { line: i + 1, expected: e[i], actual: a[i] };
    }
  }
  return null;
}

function sync() {
  let written = 0;
  let removed = 0;
  let unchanged = 0;
  for (const set of SYNC_SETS) {
    if (!fs.existsSync(set.sourceDir)) {
      console.error("source directory missing: " + set.sourceDir);
      process.exit(2);
    }
    const expected = expectedFiles(set);
    for (const [relPath, content] of expected) {
      const targetPath = path.join(set.targetDir, relPath);
      if (fs.existsSync(targetPath) && fs.readFileSync(targetPath, "utf8") === content) {
        unchanged++;
        continue;
      }
      fs.mkdirSync(path.dirname(targetPath), { recursive: true });
      fs.writeFileSync(targetPath, content);
      console.log("wrote   " + set.name + "/" + relPath);
      written++;
    }
    for (const relPath of ownedTargetFiles(set)) {
      if (!expected.has(relPath)) {
        fs.unlinkSync(path.join(set.targetDir, relPath));
        console.log("removed " + set.name + "/" + relPath + " (stale)");
        removed++;
      }
    }
  }
  console.log(
    "sync done: " + written + " written, " + removed + " removed, " + unchanged + " unchanged"
  );
}

function check() {
  const problems = [];
  for (const set of SYNC_SETS) {
    if (!fs.existsSync(set.sourceDir)) {
      problems.push("source directory missing: " + set.sourceDir);
      continue;
    }
    const expected = expectedFiles(set);
    for (const [relPath, content] of expected) {
      const targetPath = path.join(set.targetDir, relPath);
      if (!fs.existsSync(targetPath)) {
        problems.push("missing  " + set.name + "/" + relPath);
        continue;
      }
      const actual = fs.readFileSync(targetPath, "utf8");
      if (actual !== content) {
        const diff = firstDifference(content, actual);
        problems.push(
          "differs  " + set.name + "/" + relPath +
            " (first difference at line " + diff.line + ")\n" +
            "  expected: " + JSON.stringify(diff.expected) + "\n" +
            "  actual:   " + JSON.stringify(diff.actual)
        );
      }
    }
    for (const relPath of ownedTargetFiles(set)) {
      if (!expected.has(relPath)) {
        problems.push("stale    " + set.name + "/" + relPath + " (no matching source file)");
      }
    }
  }
  if (problems.length > 0) {
    console.error("unity package copies drift from csharp/ sources:");
    for (const problem of problems) {
      console.error("  " + problem);
    }
    console.error("run `node unity/sync.mjs` to regenerate.");
    process.exit(1);
  }
  console.log("unity package copies are in sync with csharp/ sources.");
}

const mode = process.argv[2];
if (mode === "--check") {
  check();
} else if (mode === undefined) {
  sync();
} else {
  console.error("usage: node unity/sync.mjs [--check]");
  process.exit(2);
}
