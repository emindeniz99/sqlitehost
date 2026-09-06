#!/usr/bin/env node
/**
 * sqlite-host-emit-manifest <entrypoint.tsp> <out-dir> [--base-name <name>]
 *
 * Compiles a SqliteHost TypeSpec compilation and writes one canonical
 * manifest (`<base-name>.manifest.json`) and DDL snapshot
 * (`<base-name>.ddl.sql`) pair into <out-dir> per @hostLibrary
 * interface. With a single library the base name defaults to
 * "sample-host" and may be overridden with --base-name; with multiple
 * libraries each base name is derived as kebab-case(interfaceName) and
 * --base-name is a usage error. Downstream emitters
 * (sqlite-host-emit-{csharp,java,typescript}) consume one manifest per
 * invocation — run them once per emitted manifest. Exits non-zero when
 * the compile or model validation reports errors.
 */

import { mkdir, writeFile } from "node:fs/promises";
import { resolve, sep } from "node:path";
import { formatDiagnostic } from "@typespec/compiler";
import { compileHostLibraries } from "@sqlite-host/codegen-core/frontend";
import {
  ddlFileName,
  emitDdl,
  emitManifest,
  libraryBaseName,
  manifestFileName,
} from "./emit.js";

function usage(): never {
  console.error(
    "usage: sqlite-host-emit-manifest <entrypoint.tsp> <out-dir> [--base-name <name>]",
  );
  console.error(
    "  Emits <base-name>.manifest.json + <base-name>.ddl.sql per @hostLibrary",
  );
  console.error(
    "  interface. Multiple libraries: base names derive from the interface",
  );
  console.error(
    "  names (kebab-case) and --base-name is rejected (single-library only).",
  );
  console.error(
    "  Run sqlite-host-emit-{csharp,java,typescript} once per manifest.",
  );
  process.exit(2);
}

/**
 * A base name is a file stem, and it is joined into <out-dir> with no
 * normalization: `--base-name ../../x` wrote the manifest two
 * directories above the out-dir and overwrote what was there without a
 * word. Restricting it to one path segment of safe characters closes
 * that, and `containedPath` below is the belt to this braces.
 */
const SAFE_FILE_STEM = /^[A-Za-z0-9_-]+$/;

/** Resolve `relative` under `outDir`, refusing to leave it. */
function containedPath(outDir: string, relative: string): string {
  const root = resolve(outDir);
  const target = resolve(root, relative);
  if (target !== root && !target.startsWith(root + sep)) {
    console.error(
      `sqlite-host-emit-manifest: refusing to write ${target}, which is outside ${root}.`,
    );
    process.exit(1);
  }
  return target;
}

const positional: string[] = [];
let baseName: string | undefined;
const args = process.argv.slice(2);
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--base-name") {
    baseName = args[++i];
    if (baseName === undefined || !SAFE_FILE_STEM.test(baseName)) {
      console.error(
        `sqlite-host-emit-manifest: --base-name must be a single file stem matching ${SAFE_FILE_STEM.source}.`,
      );
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

const result = await compileHostLibraries(entrypoint);
for (const diagnostic of result.diagnostics) {
  console.error(formatDiagnostic(diagnostic));
}
if (result.irs === undefined) {
  console.error("sqlite-host-emit-manifest: compilation failed, nothing emitted.");
  process.exit(1);
}
if (baseName !== undefined && result.irs.length > 1) {
  console.error(
    `sqlite-host-emit-manifest: --base-name applies to single-library compilations only (found ${result.irs.length} @hostLibrary interfaces).`,
  );
  usage();
}

await mkdir(outDir, { recursive: true });
const multiple = result.irs.length > 1;
for (const ir of result.irs) {
  const base = multiple ? libraryBaseName(ir) : baseName;
  const manifestPath = containedPath(outDir, manifestFileName(base));
  const ddlPath = containedPath(outDir, ddlFileName(base));
  await writeFile(manifestPath, emitManifest(ir));
  await writeFile(ddlPath, emitDdl(ir));
  console.log(manifestPath);
  console.log(ddlPath);
}
