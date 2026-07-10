#!/usr/bin/env node
/**
 * sqlite-host-emit-csharp <manifest.json> <out-dir>
 *
 * Reads a canonical host library manifest and writes the generated C#
 * sources into <out-dir> (the protocol envelope lands under
 * <out-dir>/envelope/). Prints each written path.
 *
 * Multi-library compilations: the manifest emitter writes one manifest
 * per @hostLibrary interface; run this tool once per manifest (with a
 * distinct <out-dir> each).
 */

import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { parseManifest } from "@sqlite-host/codegen-core";
import { emitCSharp } from "./emit.js";

function usage(): never {
  console.error("usage: sqlite-host-emit-csharp <manifest.json> <out-dir>");
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
if (args.length !== 2 || args.some((arg) => arg.startsWith("-"))) {
  usage();
}
const [manifestPath, outDir] = args;

const ir = parseManifest(await readFile(manifestPath, "utf8"));
for (const file of emitCSharp(ir)) {
  const target = join(outDir, file.path);
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, file.contents);
  console.log(target);
}
