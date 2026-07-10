#!/usr/bin/env node
/**
 * sqlite-host-emit-java <manifest.json> <out-dir>
 *
 * Reads a canonical SqliteHost manifest and writes the generated Java
 * sources (envelope model, host method DTO records, MethodDescriptors)
 * into <out-dir>, package directories included.
 */

import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { parseManifest } from "@sqlite-host/codegen-core";
import { emitJava } from "./emit.js";

function usage(): never {
  console.error("usage: sqlite-host-emit-java <manifest.json> <out-dir>");
  process.exit(2);
}

const args = process.argv.slice(2);
if (args.length !== 2 || args.some((a) => a.startsWith("-"))) {
  usage();
}
const [manifestPath, outDir] = args;

let files;
try {
  const ir = parseManifest(await readFile(manifestPath, "utf8"));
  files = emitJava(ir);
} catch (error) {
  console.error(`sqlite-host-emit-java: ${(error as Error).message}`);
  process.exit(1);
}

for (const file of files) {
  const target = join(outDir, file.path);
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, file.contents);
  console.log(target);
}
