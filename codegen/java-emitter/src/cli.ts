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
import { dirname, resolve, sep } from "node:path";
import { IDENTIFIER_PATTERN, parseManifest } from "@sqlite-host/codegen-core";
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

/**
 * className names the descriptor class AND its file, and the file path
 * is joined into <out-dir> with no normalization: `--class-name
 * ../../../../ESCAPED` wrote ESCAPED.java outside the out-dir with
 * `public final class ../../../../ESCAPED {` inside it. The Java
 * identifier shape closes both halves at once. Single-sourced from
 * codegen-core (docs/naming.md).
 */
const IDENTIFIER = new RegExp(IDENTIFIER_PATTERN);

/** Resolve `relative` under `outDir`, refusing to leave it. */
function containedPath(outDir: string, relative: string): string {
  const root = resolve(outDir);
  const target = resolve(root, relative);
  if (target !== root && !target.startsWith(root + sep)) {
    console.error(
      `sqlite-host-emit-java: refusing to write ${target}, which is outside ${root}.`,
    );
    process.exit(1);
  }
  return target;
}

const positionals: string[] = [];
let className: string | undefined;
const args = process.argv.slice(2);
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--class-name") {
    className = args[++i];
    if (className === undefined || !IDENTIFIER.test(className)) {
      console.error(
        `sqlite-host-emit-java: --class-name must be a Java identifier matching ${IDENTIFIER_PATTERN}.`,
      );
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
  const target = containedPath(outDir, file.path);
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, file.contents);
  console.log(target);
}
