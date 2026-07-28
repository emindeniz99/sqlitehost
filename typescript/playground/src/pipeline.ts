/**
 * The playground pipeline: one .tsp source string in, either the
 * diagnostics that stopped it or every generated artifact out. This is
 * the same frontend + emitters the CLI codegen uses (codegen/core plus
 * the four @sqlite-host/emitter-* packages); only the CompilerHost
 * differs, which is what makes the browser output byte-identical to a
 * `pnpm run build` on disk (src/test/parity.test.ts pins that).
 */

import { getSourceLocation, type Diagnostic } from "@typespec/compiler";
import { compileHostLibrary } from "@sqlite-host/codegen-core/frontend";
import { generateSchemaScript, type HostLibraryIr } from "@sqlite-host/codegen-core";
import { ddlFileName, emitManifest, manifestFileName } from "@sqlite-host/emitter-manifest";
import { emitCSharp, type CSharpProfile } from "@sqlite-host/emitter-csharp";
import { emitJava } from "@sqlite-host/emitter-java";
import { emitTypeScript } from "@sqlite-host/emitter-typescript";
import { createBrowserCompilerHost, ENTRYPOINT } from "./browser-host.js";

/** The three C# size profiles, in the order the UI offers them. */
export const CSHARP_PROFILES: readonly CSharpProfile[] = ["classic", "compact", "ultra"];

// Artifact names for the two single-document tabs, taken from the
// emitter rather than restated, so the UI cannot drift from what a CLI
// emit would write.
export const MANIFEST_FILE_NAME = manifestFileName();
export const DDL_FILE_NAME = ddlFileName();

export interface PlaygroundFile {
  path: string;
  contents: string;
}

export interface PlaygroundDiagnostic {
  severity: string;
  code: string;
  message: string;
  /** 1-based source position, absent for diagnostics with no location. */
  line?: number;
  column?: number;
}

export interface PlaygroundOutput {
  manifest: string;
  ddl: string;
  /** Emitted C# sources per size profile, keyed by CSHARP_PROFILES. */
  csharp: Record<CSharpProfile, PlaygroundFile[]>;
  java: PlaygroundFile[];
  typescript: PlaygroundFile[];
}

export type PlaygroundResult =
  | { ok: true; ir: HostLibraryIr; output: PlaygroundOutput; diagnostics: PlaygroundDiagnostic[] }
  | { ok: false; diagnostics: PlaygroundDiagnostic[] };

/**
 * Flatten a TypeSpec diagnostic to the plain, DOM-friendly shape the UI
 * renders. getSourceLocation resolves the whole range of target kinds
 * (AST node, type, symbol) to a file and offset; diagnostics targeting
 * NoTarget legitimately have no position, and keep none here.
 */
function toPlaygroundDiagnostic(diagnostic: Diagnostic): PlaygroundDiagnostic {
  const flat: PlaygroundDiagnostic = {
    severity: diagnostic.severity,
    code: diagnostic.code,
    message: diagnostic.message,
  };
  const location = getSourceLocation(diagnostic.target);
  if (location?.file === undefined) {
    return flat;
  }
  const { line, character } = location.file.getLineAndCharacterOfPosition(location.pos);
  return { ...flat, line: line + 1, column: character + 1 };
}

/**
 * Compile `source` and run every emitter over the resulting IR.
 * Compilation problems come back as `ok: false` with diagnostics —
 * they are the normal outcome of a half-typed editor buffer, not an
 * exceptional condition.
 */
export async function runPipeline(source: string): Promise<PlaygroundResult> {
  const host = createBrowserCompilerHost(source);
  const { ir, diagnostics } = await compileHostLibrary(ENTRYPOINT, host);
  const reported = diagnostics.map(toPlaygroundDiagnostic);
  if (ir === undefined) {
    return { ok: false, diagnostics: reported };
  }
  return { ok: true, ir, output: emitAll(ir), diagnostics: reported };
}

/** Run every emitter over an already normalized IR. */
function emitAll(ir: HostLibraryIr): PlaygroundOutput {
  const csharp = {} as Record<CSharpProfile, PlaygroundFile[]>;
  for (const profile of CSHARP_PROFILES) {
    csharp[profile] = emitCSharp(ir, { profile });
  }
  return {
    manifest: emitManifest(ir),
    ddl: generateSchemaScript(ir),
    csharp,
    java: emitJava(ir),
    typescript: emitTypeScript(ir),
  };
}
