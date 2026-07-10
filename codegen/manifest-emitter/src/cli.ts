#!/usr/bin/env node
/**
 * sqlite-host-emit-manifest <entrypoint.tsp> <out-dir> [--base-name <name>]
 *
 * Compiles a SqliteHost TypeSpec host library and writes the canonical
 * manifest (`<base-name>.manifest.json`) and DDL snapshot
 * (`<base-name>.ddl.sql`) into <out-dir>. Exits non-zero when the
 * compile or model validation reports errors.
 */

import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { formatDiagnostic } from "@typespec/compiler";
import { compileHostLibrary } from "@sqlite-host/codegen-core/frontend";
import { ddlFileName, emitDdl, emitManifest, manifestFileName } from "./emit.js";

function usage(): never {
  console.error(
    "usage: sqlite-host-emit-manifest <entrypoint.tsp> <out-dir> [--base-name <name>]",
  );
  process.exit(2);
}

const positional: string[] = [];
let baseName: string | undefined;
const args = process.argv.slice(2);
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--base-name") {
    baseName = args[++i];
    if (baseName === undefined) {
      usage();
    }
  } else if (args[i].startsWith("-")) {
    usage();
  } else {
    positional.push(args[i]);
  }
}
if (positional.length !== 2) {
  usage();
}
const [entrypoint, outDir] = positional;

const result = await compileHostLibrary(entrypoint);
for (const diagnostic of result.diagnostics) {
  console.error(formatDiagnostic(diagnostic));
}
if (result.ir === undefined) {
  console.error("sqlite-host-emit-manifest: compilation failed, nothing emitted.");
  process.exit(1);
}

await mkdir(outDir, { recursive: true });
const manifestPath = join(outDir, manifestFileName(baseName));
const ddlPath = join(outDir, ddlFileName(baseName));
await writeFile(manifestPath, emitManifest(result.ir));
await writeFile(ddlPath, emitDdl(result.ir));
console.log(manifestPath);
console.log(ddlPath);
