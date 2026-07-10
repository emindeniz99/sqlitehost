import { createTypeSpecLibrary, type JSONSchemaType } from "@typespec/compiler";

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
  diagnostics: {},
  emitter: {
    options: EmitterOptionsSchema,
  },
});
