#!/usr/bin/env node
/**
 * sqlite-host-emit-csharp <manifest.json> <out-dir>
 *     [--profile <classic|compact|ultra>] [--namespace <ns>] [--dto-fields]
 *
 * Reads a canonical host library manifest and writes the generated C#
 * sources into <out-dir> (the protocol envelope lands under
 * <out-dir>/envelope/). Prints each written path. --profile selects the
 * code-size profile (default classic); --namespace overrides the
 * generated namespace in every emitted file; --dto-fields emits DTO
 * members as public fields instead of auto-properties (recommended for
 * Unity IL2CPP targets — docs/reports/il2cpp-size-report.md).
 *
 * Multi-library compilations: the manifest emitter writes one manifest
 * per @hostLibrary interface; run this tool once per manifest (with a
 * distinct <out-dir> each).
 */

import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { parseManifest } from "@sqlite-host/codegen-core";
import { emitCSharp, type CSharpProfile } from "./emit.js";

function usage(): never {
  console.error(
    "usage: sqlite-host-emit-csharp <manifest.json> <out-dir> [--profile <classic|compact|ultra>] [--namespace <ns>] [--dto-fields]",
  );
  console.error(
    "  Takes one manifest per invocation. Multi-library compilations produce",
  );
  console.error(
    "  one manifest per @hostLibrary (see sqlite-host-emit-manifest); run",
  );
  console.error("  this tool once per manifest.");
  process.exit(2);
}

const args = process.argv.slice(2);
const positionals: string[] = [];
let profile: CSharpProfile = "classic";
let namespaceOverride: string | undefined;
let dtoFields = false;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--profile") {
    const value = args[++i];
    if (value !== "classic" && value !== "compact" && value !== "ultra") {
      usage();
    }
    profile = value;
  } else if (arg === "--namespace") {
    const value = args[++i];
    if (value === undefined) {
      usage();
    }
    namespaceOverride = value;
  } else if (arg === "--dto-fields") {
    dtoFields = true;
  } else if (arg.startsWith("-")) {
    usage();
  } else {
    positionals.push(arg);
  }
}
if (positionals.length !== 2) {
  usage();
}
const [manifestPath, outDir] = positionals;

const ir = parseManifest(await readFile(manifestPath, "utf8"));
for (const file of emitCSharp(ir, { profile, namespaceOverride, dtoFields })) {
  const target = join(outDir, file.path);
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, file.contents);
  console.log(target);
}
