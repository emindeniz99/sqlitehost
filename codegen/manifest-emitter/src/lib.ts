import {
  createTypeSpecLibrary,
  paramMessage,
  type JSONSchemaType,
} from "@typespec/compiler";

export interface ManifestEmitterOptions {
  /**
   * Base name for the emitted files: `<base-name>.manifest.json` and
   * `<base-name>.ddl.sql`. Defaults to "sample-host", the fixture family
   * name used by the committed keystone snapshots.
   */
  "base-name"?: string;
}

const EmitterOptionsSchema: JSONSchemaType<ManifestEmitterOptions> = {
  type: "object",
  additionalProperties: false,
  properties: {
    "base-name": { type: "string", nullable: true },
  },
  required: [],
};

export const $lib = createTypeSpecLibrary({
  name: "@sqlite-host/emitter-manifest",
  diagnostics: {
    "base-name-multiple-libraries": {
      severity: "error",
      messages: {
        default: paramMessage`base-name applies to single-library compilations only; this compilation defines ${"count"} @hostLibrary interfaces, whose base names derive from their interface names (kebab-case).`,
      },
    },
  },
  emitter: {
    options: EmitterOptionsSchema,
  },
});

export const { reportDiagnostic } = $lib;
