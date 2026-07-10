/**
 * Standard TypeSpec emitter entry: normalizes the compiled program via
 * the codegen-core frontend and writes the canonical manifest + DDL
 * snapshot into the emitter output directory — one pair per
 * @hostLibrary interface. With multiple libraries the base names derive
 * from the interface names (kebab-case) and the base-name option is
 * rejected (single-library only).
 */

import {
  emitFile,
  NoTarget,
  resolvePath,
  type EmitContext,
} from "@typespec/compiler";
import { buildHostLibraryIrs } from "@sqlite-host/codegen-core/frontend";
import {
  ddlFileName,
  emitDdl,
  emitManifest,
  libraryBaseName,
  manifestFileName,
} from "./emit.js";
import { reportDiagnostic, type ManifestEmitterOptions } from "./lib.js";

export async function $onEmit(
  context: EmitContext<ManifestEmitterOptions>,
): Promise<void> {
  const irs = buildHostLibraryIrs(context.program);
  if (irs === undefined || context.program.compilerOptions.noEmit) {
    return;
  }
  const baseNameOption = context.options["base-name"];
  if (baseNameOption !== undefined && irs.length > 1) {
    reportDiagnostic(context.program, {
      code: "base-name-multiple-libraries",
      format: { count: String(irs.length) },
      target: NoTarget,
    });
    return;
  }
  for (const ir of irs) {
    const baseName = irs.length > 1 ? libraryBaseName(ir) : baseNameOption;
    await emitFile(context.program, {
      path: resolvePath(context.emitterOutputDir, manifestFileName(baseName)),
      content: emitManifest(ir),
    });
    await emitFile(context.program, {
      path: resolvePath(context.emitterOutputDir, ddlFileName(baseName)),
      content: emitDdl(ir),
    });
  }
}
