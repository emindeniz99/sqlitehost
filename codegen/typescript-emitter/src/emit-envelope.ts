/**
 * Emit `envelope.ts` — the protocol-level script envelope contract
 * (Script/Step/Statement/RuntimeInput plus the BindingValue
 * discriminated union). Everything is derived from `ir.scriptEnvelope`
 * (engine, binding types), `ir.inputsTable`, and `ir.manifestVersion`;
 * the output is identical for every host library on the same protocol.
 */

import type { HostLibraryIr } from "@sqlite-host/codegen-core";
import { docComment, generatedHeader } from "./format.js";

interface BindingTypeSpec {
  doc: string;
  /** TypeScript type of the JSON `value` property; absent for `null`. */
  valueType?: string;
}

/** Protocol-defined docs and value types per binding discriminator. */
const BINDING_TYPE_SPECS: Record<string, BindingTypeSpec> = {
  null: { doc: "SQLite NULL. The JSON `value` property is absent." },
  int32: { doc: "SQLite INTEGER from an int32.", valueType: "Int32Value" },
  int64: { doc: "SQLite INTEGER from an int64.", valueType: "Int64Value" },
  bool: { doc: "SQLite INTEGER 1 / 0.", valueType: "boolean" },
  text: { doc: "SQLite TEXT.", valueType: "string" },
  blob: {
    doc:
      "SQLite BLOB. The JSON `value` is a base64 string (standard " +
      "alphabet, padding, no line breaks).",
    valueType: "string",
  },
  float32: {
    doc:
      "SQLite REAL from a float32. The JSON `value` is a finite number " +
      "representable as an IEEE-754 single (round-to-nearest); the " +
      "string form is not accepted.",
    valueType: "number",
  },
  float64: {
    doc:
      "SQLite REAL from a float64. The JSON `value` is a finite number; " +
      "the string form is not accepted.",
    valueType: "number",
  },
};

function bindingInterfaceName(bindingType: string): string {
  return `${bindingType[0].toUpperCase()}${bindingType.slice(1)}BindingValue`;
}

/** Render the envelope contract module for the IR's protocol. */
export function emitEnvelope(ir: HostLibraryIr): string {
  const version = `v${ir.manifestVersion}`;
  const engineLiteral = JSON.stringify(ir.scriptEnvelope.engine);
  const bindingTypes = ir.scriptEnvelope.bindingTypes;
  const inputsTable = ir.inputsTable.name;
  for (const bindingType of bindingTypes) {
    if (BINDING_TYPE_SPECS[bindingType] === undefined) {
      throw new Error(
        `typescript-emitter: unknown binding type "${bindingType}" in scriptEnvelope.bindingTypes.`,
      );
    }
  }

  const parts: string[] = [];
  parts.push(
    generatedHeader(
      `SqliteHost script envelope contract (protocol ${version}). ` +
        "Generated from the SqliteHost protocol TypeSpec model " +
        "(typespec/library). Do not edit by hand — this vendored copy is " +
        "golden-tested against fresh emitter output. See " +
        "docs/script-envelope.md for the normative JSON shape.",
    ),
  );

  parts.push(
    [
      docComment(`The only engine identifier accepted by protocol ${version}.`),
      `export const SCRIPT_ENGINE_${version.toUpperCase()} = ${engineLiteral};`,
    ].join("\n"),
  );

  parts.push(
    [
      docComment("Binding type discriminators, in canonical manifest order."),
      "export const BINDING_TYPES = [",
      ...bindingTypes.map((bindingType) => `  ${JSON.stringify(bindingType)},`),
      "] as const;",
    ].join("\n"),
  );

  parts.push("export type BindingType = (typeof BINDING_TYPES)[number];");

  if (bindingTypes.includes("int64")) {
    parts.push(
      [
        docComment(
          "JSON representation of an int64: a number when |v| <= 2^53 - 1, " +
            "otherwise a decimal string. Parsers accept both forms.",
        ),
        "export type Int64Value = number | string;",
      ].join("\n"),
    );
  }

  if (bindingTypes.includes("int32")) {
    parts.push(
      [
        docComment(
          "JSON representation of an int32: a number (or decimal string) " +
            "in int32 range.",
        ),
        "export type Int32Value = number | string;",
      ].join("\n"),
    );
  }

  for (const bindingType of bindingTypes) {
    const spec = BINDING_TYPE_SPECS[bindingType];
    const lines = [
      docComment(spec.doc),
      `export interface ${bindingInterfaceName(bindingType)} {`,
      `  type: ${JSON.stringify(bindingType)};`,
    ];
    if (spec.valueType !== undefined) {
      lines.push(`  value: ${spec.valueType};`);
    }
    lines.push("}");
    parts.push(lines.join("\n"));
  }

  parts.push(
    [
      docComment("A typed binding value, discriminated by `type`."),
      "export type BindingValue =",
      bindingTypes
        .map((bindingType) => `  | ${bindingInterfaceName(bindingType)}`)
        .join("\n") + ";",
    ].join("\n"),
  );

  parts.push(
    [
      docComment(
        `A runtime input inserted into the \`${inputsTable}\` table before ` +
          "the first step executes.",
      ),
      "export interface RuntimeInput {",
      "  name: string;",
      "  value: BindingValue;",
      "}",
    ].join("\n"),
  );

  parts.push(
    [
      docComment(
        "One SQL statement. Binding names are bare (no prefix); in SQL, " +
          "named parameters may be written `:name`, `@name`, or `$name`.",
      ),
      "export interface Statement {",
      "  sql: string;",
      "  bindings?: Record<string, BindingValue>;",
      "}",
    ].join("\n"),
  );

  parts.push(
    [
      docComment(
        "One ordered step. The runtime drains pending host calls only " +
          "after all statements in the step succeeded.",
      ),
      "export interface Step {",
      "  id: string;",
      "  statements: Statement[];",
      "}",
    ].join("\n"),
  );

  parts.push(
    [
      docComment(`The script envelope (protocol ${version}).`),
      "export interface Script {",
      docComment(`Must be \`${engineLiteral}\`.`, "  "),
      "  engine: string;",
      docComment("Opaque identifier for diagnostics.", "  "),
      "  scriptId?: string;",
      docComment("Integer >= 1.", "  "),
      "  requiredApiLevel: number;",
      docComment("Subset of the host's supported features, else clean skip.", "  "),
      "  requiredFeatures?: string[];",
      docComment("Methods the script uses; missing method -> clean skip.", "  "),
      "  requiredMethods?: string[];",
      docComment(
        `Runtime inputs inserted into \`${inputsTable}\` before step 1.`,
        "  ",
      ),
      "  inputs?: RuntimeInput[];",
      docComment("Ordered steps; ids must be unique and non-empty.", "  "),
      "  steps: Step[];",
      "}",
    ].join("\n"),
  );

  return parts.join("\n\n") + "\n";
}
