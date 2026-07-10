/**
 * Programmatic emit API: map a HostLibraryIr to the generated
 * TypeScript source files. Paths are relative to the repo's
 * `typescript/` workspace so the byte-golden tests (and the CLI) can
 * mirror the vendored layout.
 */

import type { HostLibraryIr } from "@sqlite-host/codegen-core";
import { emitEnvelope } from "./emit-envelope.js";
import { emitHostTypes } from "./emit-host-types.js";

export interface EmittedFile {
  /** Path relative to the `typescript/` workspace root. */
  path: string;
  contents: string;
}

export interface EmitTypeScriptOptions {
  /**
   * Fixture family base name (e.g. "sample-host"); names the authoring
   * module file, its metadata const, and the paths in its header.
   */
  baseName?: string;
}

/** Fixture family base name used when no explicit base name is given. */
export const DEFAULT_BASE_NAME = "sample-host";

/** Vendored location of the protocol envelope contract. */
export const ENVELOPE_FILE_PATH = "runtime-types/src/generated/envelope.ts";

/** Vendored location of the per-host authoring module. */
export function hostTypesFilePath(baseName: string = DEFAULT_BASE_NAME): string {
  return `authoring-sdk/src/generated/${baseName}.ts`;
}

/** Emit every generated TypeScript source for the host library IR. */
export function emitTypeScript(
  ir: HostLibraryIr,
  options: EmitTypeScriptOptions = {},
): EmittedFile[] {
  const baseName = options.baseName ?? DEFAULT_BASE_NAME;
  return [
    { path: ENVELOPE_FILE_PATH, contents: emitEnvelope(ir) },
    { path: hostTypesFilePath(baseName), contents: emitHostTypes(ir, baseName) },
  ];
}
