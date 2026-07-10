/** Shared helpers for frontend tests: paths + inline .tsp compilation. */

import { strict as assert } from "node:assert";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { compileHostLibrary, type FrontendResult } from "../frontend.js";

export const packageRoot = resolve(fileURLToPath(import.meta.url), "../../..");
export const projectRoot = resolve(packageRoot, "../..");
export const samplePath = join(
  projectRoot,
  "typespec/examples/sample-host-methods.tsp",
);
export const manifestFixturePath = join(
  projectRoot,
  "fixtures/manifests/sample-host.manifest.json",
);
export const ddlFixturePath = join(
  projectRoot,
  "fixtures/schemas/sample-host.ddl.sql",
);

let counter = 0;

/**
 * Compile an inline TypeSpec source. The file is written inside this
 * package (gitignored .tsp-output/) so `import "@sqlite-host/typespec"`
 * resolves through the workspace node_modules.
 */
export async function compileSource(source: string): Promise<FrontendResult> {
  const dir = join(packageRoot, ".tsp-output", "frontend-tests");
  mkdirSync(dir, { recursive: true });
  const file = join(dir, `case-${process.pid}-${counter++}.tsp`);
  writeFileSync(file, source);
  try {
    return await compileHostLibrary(file);
  } finally {
    rmSync(file, { force: true });
  }
}

/** Assert the result failed with the given @sqlite-host/typespec diagnostic. */
export function assertDiagnostic(result: FrontendResult, code: string): void {
  assert.equal(
    result.ir,
    undefined,
    `expected no IR when diagnostic ${code} is reported`,
  );
  const full = `@sqlite-host/typespec/${code}`;
  const codes = result.diagnostics.map((d) => d.code);
  assert.ok(
    codes.includes(full),
    `expected diagnostic ${full}, got: ${codes.join(", ") || "(none)"}`,
  );
}
