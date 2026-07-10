/**
 * Standard TypeSpec emitter entry: normalizes the compiled program via
 * the codegen-core frontend and writes the canonical manifest + DDL
 * snapshot into the emitter output directory.
 */

import { emitFile, resolvePath, type EmitContext } from "@typespec/compiler";
import { buildHostLibraryIr } from "@sqlite-host/codegen-core/frontend";
import { ddlFileName, emitDdl, emitManifest, manifestFileName } from "./emit.js";
import type { ManifestEmitterOptions } from "./lib.js";

export async function $onEmit(
  context: EmitContext<ManifestEmitterOptions>,
): Promise<void> {
  const ir = buildHostLibraryIr(context.program);
  if (ir === undefined || context.program.compilerOptions.noEmit) {
    return;
  }
  const baseName = context.options["base-name"];
  await emitFile(context.program, {
    path: resolvePath(context.emitterOutputDir, manifestFileName(baseName)),
    content: emitManifest(ir),
  });
  await emitFile(context.program, {
    path: resolvePath(context.emitterOutputDir, ddlFileName(baseName)),
    content: emitDdl(ir),
  });
}
