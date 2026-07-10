/** Shared helpers for frontend tests: paths + inline .tsp compilation. */

import { strict as assert } from "node:assert";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  compileHostLibraries,
  compileHostLibrary,
  type FrontendLibrariesResult,
  type FrontendResult,
} from "../frontend.js";

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
  const file = writeCase(source);
  try {
    return await compileHostLibrary(file);
  } finally {
    rmSync(file, { force: true });
  }
}

/** Compile an inline TypeSpec source with the multi-library frontend API. */
export async function compileSourceAll(
  source: string,
): Promise<FrontendLibrariesResult> {
  const file = writeCase(source);
  try {
    return await compileHostLibraries(file);
  } finally {
    rmSync(file, { force: true });
  }
}

function writeCase(source: string): string {
  const dir = join(packageRoot, ".tsp-output", "frontend-tests");
  mkdirSync(dir, { recursive: true });
  const file = join(dir, `case-${process.pid}-${counter++}.tsp`);
  writeFileSync(file, source);
  return file;
}

/** Assert the result failed with the given @sqlite-host/typespec diagnostic. */
export function assertDiagnostic(result: FrontendResult, code: string): void {
  assert.equal(
    result.ir,
    undefined,
    `expected no IR when diagnostic ${code} is reported`,
  );
  assertDiagnosticCode(result.diagnostics, code);
}

/** Plural-API variant of assertDiagnostic. */
export function assertLibrariesDiagnostic(
  result: FrontendLibrariesResult,
  code: string,
): void {
  assert.equal(
    result.irs,
    undefined,
    `expected no IRs when diagnostic ${code} is reported`,
  );
  assertDiagnosticCode(result.diagnostics, code);
}

function assertDiagnosticCode(
  diagnostics: FrontendResult["diagnostics"],
  code: string,
): void {
  const full = `@sqlite-host/typespec/${code}`;
  const codes = diagnostics.map((d) => d.code);
  assert.ok(
    codes.includes(full),
    `expected diagnostic ${full}, got: ${codes.join(", ") || "(none)"}`,
  );
}
