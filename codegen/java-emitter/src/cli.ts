#!/usr/bin/env node
/**
 * sqlite-host-emit-java <manifest.json> <out-dir> [--class-name <name>]
 *
 * Reads a canonical SqliteHost manifest and writes the generated Java
 * sources (envelope model, host method DTO records, the method-descriptor
 * class) into <out-dir>, package directories included. --class-name names
 * that descriptor class and its file (default MethodDescriptors).
 *
 * Multi-library compilations: the manifest emitter writes one manifest
 * per @hostLibrary interface; run this tool once per manifest, with a
 * distinct --class-name or a distinct <out-dir>. Two libraries sharing a
 * namespace land in the same generated package, and Java ties the file
 * name to the class name, so without one of those the second run
 * overwrites the first.
 */

import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { parseManifest } from "@sqlite-host/codegen-core";
import { DEFAULT_DESCRIPTORS_CLASS_NAME, emitJava } from "./emit.js";

function usage(): never {
  console.error(
    "usage: sqlite-host-emit-java <manifest.json> <out-dir> [--class-name <name>]",
  );
  console.error(
    "  Takes one manifest per invocation. Multi-library compilations produce",
  );
  console.error(
    "  one manifest per @hostLibrary (see sqlite-host-emit-manifest); run",
  );
  console.error(
    "  this tool once per manifest, with a distinct --class-name or out-dir.",
  );
  process.exit(2);
}

const positionals: string[] = [];
let className: string | undefined;
const args = process.argv.slice(2);
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--class-name") {
    className = args[++i];
    if (className === undefined) {
      usage();
    }
  } else if (args[i].startsWith("-")) {
    usage();
  } else {
    positionals.push(args[i]);
  }
}
if (positionals.length !== 2) {
  usage();
}
const [manifestPath, outDir] = positionals;

let files;
try {
  const ir = parseManifest(await readFile(manifestPath, "utf8"));
  files = emitJava(ir, {
    className: className ?? DEFAULT_DESCRIPTORS_CLASS_NAME,
  });
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
