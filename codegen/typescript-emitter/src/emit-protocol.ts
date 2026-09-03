/**
 * Emit `protocol.ts` — the host-independent protocol constants the
 * TypeScript authoring lint consumes, projected from the single source in
 * `codegen/core/src/ir.ts` (docs/proposals/rule-parameters-as-data.md),
 * exactly as `Protocol.java` and `ProtocolConstants.g.cs` are.
 *
 * The authoring SDK is published to npm and codegen-core is not, so the
 * lint cannot import the constants at runtime; a vendored projection is
 * the same answer the C# and Java ports already use, and the byte-golden
 * check keeps it from drifting.
 */

import {
  BINDING_TYPE_COMPAT,
  ENGINE_V1,
  FEATURE_INLINE_FUNCTIONS,
  FORBIDDEN_LEADING_KEYWORDS,
  FUNCTION_MIN_VERSION,
  FUNCTION_PREFIX_MIN_VERSION,
  NONDETERMINISTIC_FUNCTIONS_ALWAYS,
  NONDETERMINISTIC_TIME_FUNCTIONS,
  NONPORTABLE_FUNCTIONS,
  type ScalarTypeIr,
} from "@sqlite-host/codegen-core";
import { docComment, generatedHeader, renderLiteral, type Literal } from "./format.js";

/**
 * Scalar column types in manifest order, so the emitted compatibility
 * table does not inherit an object-literal iteration order.
 */
const SCALAR_ORDER: ScalarTypeIr[] = [
  "int32",
  "int64",
  "boolean",
  "string",
  "bytes",
  "float32",
  "float64",
];

/** `export const NAME: TYPE = <literal>;` preceded by its doc comment. */
function constant(name: string, type: string, value: Literal, doc: string): string {
  const prefix = `export const ${name}: ${type} = `;
  return `${docComment(doc)}\n${prefix}${renderLiteral(value, "", prefix.length, 1)};`;
}

/** Render the protocol constants module. Zero-arg: nothing here is per-host. */
export function emitProtocol(): string {
  const compat: Record<string, Literal> = {};
  for (const scalar of SCALAR_ORDER) {
    compat[scalar] = [...BINDING_TYPE_COMPAT[scalar]];
  }

  const blocks = [
    generatedHeader(
      `Protocol constants projected from codegen/core/src/ir.ts (protocol ` +
        `${ENGINE_V1}, docs/proposals/rule-parameters-as-data.md). The ` +
        `authoring SDK publishes to npm and codegen-core does not, so the ` +
        `lint reads this vendored projection instead of importing the ` +
        `source; the cross-language golden pins the bytes. Do not edit by ` +
        `hand.`,
    ),
    constant(
      "FEATURE_INLINE_FUNCTIONS",
      "string",
      FEATURE_INLINE_FUNCTIONS,
      "Feature flag a script must declare in `requiredFeatures` to call a " +
        "host's inline functions; the lint gates inline-function use on its " +
        "presence.",
    ),
    constant(
      "BINDING_TYPE_COMPAT",
      "Readonly<Record<string, readonly string[]>>",
      compat,
      "Binding-type compatibility: for each scalar column type (manifest " +
        "wire name), the envelope binding value types that may feed it. A " +
        "`null` binding is accepted iff the column is optional — that rule " +
        "stays with the caller.",
    ),
    constant(
      "NONDETERMINISTIC_FUNCTIONS_ALWAYS",
      "readonly string[]",
      [...NONDETERMINISTIC_FUNCTIONS_ALWAYS],
      "Built-ins that return a different value on every evaluation: every " +
        "call is flagged by the determinism lint, whatever its arguments. " +
        "Names are compared lowercased.",
    ),
    constant(
      "NONDETERMINISTIC_TIME_FUNCTIONS",
      "readonly string[]",
      [...NONDETERMINISTIC_TIME_FUNCTIONS],
      "Date/time built-ins that are nondeterministic only when they read " +
        "the wall clock — called with no arguments, or with the time value " +
        "`'now'`.",
    ),
    constant(
      "FUNCTION_MIN_VERSION",
      "Readonly<Record<string, number>>",
      { ...FUNCTION_MIN_VERSION },
      "SQLite built-ins introduced above the default contract floor, keyed " +
        "by the SQLITE_VERSION_NUMBER of the release that added them. The " +
        "lint compares each entry against the host manifest's " +
        "`library.minSqliteVersionNumber`.",
    ),
    constant(
      "FUNCTION_PREFIX_MIN_VERSION",
      "Readonly<Record<string, number>>",
      { ...FUNCTION_PREFIX_MIN_VERSION },
      "Version floors for whole function families, keyed by name prefix — " +
        "the LONGEST matching prefix wins. Covers the JSON surface, too " +
        "large to enumerate by hand without drift.",
    ),
    constant(
      "NONPORTABLE_FUNCTIONS",
      "readonly string[]",
      [...NONPORTABLE_FUNCTIONS],
      "Built-ins whose presence is decided by the device engine's compile " +
        "options rather than its version, so no version floor can make them " +
        "safe.",
    ),
    constant(
      "FORBIDDEN_LEADING_KEYWORDS",
      "readonly string[]",
      [...FORBIDDEN_LEADING_KEYWORDS],
      "Statement kinds a script may not use, matched on the statement's " +
        "first meaningful token.",
    ),
  ];

  return `${blocks.join("\n\n")}\n`;
}
