#!/usr/bin/env node
/**
 * sqlite-host-emit-typescript <manifest.json> <out-dir> [--base-name <name>]
 *
 * Reads a canonical SqliteHost manifest and writes the generated
 * TypeScript sources (protocol envelope contract + per-host authoring
 * module) under <out-dir>, mirroring the vendored `typescript/` layout.
 *
 * Multi-library compilations: the manifest emitter writes one manifest
 * per @hostLibrary interface; run this tool once per manifest, passing
 * a per-library --base-name so the authoring modules do not collide.
 */

import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve, sep } from "node:path";
import { parseManifest } from "@sqlite-host/codegen-core";
import { DEFAULT_BASE_NAME, emitTypeScript } from "./emit.js";

function usage(): never {
  console.error(
    "usage: sqlite-host-emit-typescript <manifest.json> <out-dir> [--base-name <name>]",
  );
  console.error(
    "  Takes one manifest per invocation. Multi-library compilations produce",
  );
  console.error(
    "  one manifest per @hostLibrary (see sqlite-host-emit-manifest); run",
  );
  console.error(
    "  this tool once per manifest with a per-library --base-name.",
  );
  process.exit(2);
}

/**
 * A base name is both a file stem and the seed of a generated
 * identifier (metadataConstName: "sample-host" -> SAMPLE_HOST_METADATA),
 * so it must be a safe single path segment AND must not start with a
 * digit -- `--base-name 1x` produced `export const 1X_METADATA`, which
 * is not a TypeScript identifier. Unvalidated, it was also joined into
 * <out-dir> with no normalization and wrote the module outside it.
 */
const SAFE_BASE_NAME = /^[A-Za-z_][A-Za-z0-9_-]*$/;

/** Resolve `relative` under `outDir`, refusing to leave it. */
function containedPath(outDir: string, relative: string): string {
  const root = resolve(outDir);
  const target = resolve(root, relative);
  if (target !== root && !target.startsWith(root + sep)) {
    console.error(
      `sqlite-host-emit-typescript: refusing to write ${target}, which is outside ${root}.`,
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
    if (baseName === undefined || !SAFE_BASE_NAME.test(baseName)) {
      console.error(
        `sqlite-host-emit-typescript: --base-name must match ${SAFE_BASE_NAME.source} (it becomes a file stem and a generated identifier).`,
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
const [manifestPath, outDir] = positional;

/**
 * Read and validate the manifest. parseManifest reports a hand-edited
 * or merge-conflicted manifest as one aggregated error listing every
 * problem with its JSON path; that message is the useful output, so it
 * is printed rather than thrown as a Node stack trace.
 */
async function readIr(path: string) {
  try {
    return parseManifest(await readFile(path, "utf8"));
  } catch (error) {
    console.error(`sqlite-host-emit-typescript: ${(error as Error).message}`);
    process.exit(1);
  }
}

const ir = await readIr(manifestPath);
for (const file of emitTypeScript(ir, {
  baseName: baseName ?? DEFAULT_BASE_NAME,
})) {
  const outPath = containedPath(outDir, file.path);
  await mkdir(dirname(outPath), { recursive: true });
  await writeFile(outPath, file.contents);
  console.log(outPath);
}
