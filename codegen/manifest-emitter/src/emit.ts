/**
 * Programmatic emit API. Thin, deterministic wrappers over codegen-core's
 * canonical serializers — the emitter never derives names or re-orders
 * keys itself (docs/manifest.md).
 */

import {
  generateSchemaScript,
  serializeManifest,
  type HostLibraryIr,
} from "@sqlite-host/codegen-core";

/** Fixture family base name used when no explicit base name is given. */
export const DEFAULT_BASE_NAME = "sample-host";

/** Canonical manifest JSON bytes (pinned key order, LF, trailing newline). */
export function emitManifest(ir: HostLibraryIr): string {
  return serializeManifest(ir);
}

/** Canonical DDL snapshot bytes (byte-identical to fixtures/schemas). */
export function emitDdl(ir: HostLibraryIr): string {
  return generateSchemaScript(ir);
}

export function manifestFileName(baseName: string = DEFAULT_BASE_NAME): string {
  return `${baseName}.manifest.json`;
}

export function ddlFileName(baseName: string = DEFAULT_BASE_NAME): string {
  return `${baseName}.ddl.sql`;
}
