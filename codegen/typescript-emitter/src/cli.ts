#!/usr/bin/env node
/**
 * sqlite-host-emit-typescript <manifest.json> <out-dir> [--base-name <name>]
 *
 * Reads a canonical SqliteHost manifest and writes the generated
 * TypeScript sources (protocol envelope contract + per-host authoring
 * module) under <out-dir>, mirroring the vendored `typescript/` layout.
 */

import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { parseManifest } from "@sqlite-host/codegen-core";
import { DEFAULT_BASE_NAME, emitTypeScript } from "./emit.js";

function usage(): never {
  console.error(
    "usage: sqlite-host-emit-typescript <manifest.json> <out-dir> [--base-name <name>]",
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
const [manifestPath, outDir] = positional;

const ir = parseManifest(await readFile(manifestPath, "utf8"));
for (const file of emitTypeScript(ir, {
  baseName: baseName ?? DEFAULT_BASE_NAME,
})) {
  const outPath = join(outDir, file.path);
  await mkdir(dirname(outPath), { recursive: true });
  await writeFile(outPath, file.contents);
  console.log(outPath);
}
