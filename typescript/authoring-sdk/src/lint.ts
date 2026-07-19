/**
 * Static authoring lint — the `typescript` validator subset of
 * docs/validation.md, pinned by fixtures/payloads/expectations.json.
 * No SQLite: structural checks, binding checks via the shared lexical
 * scanner, and best-effort host-call usage checks with static call-id
 * resolution (literals and text bindings; computed ids are skipped).
 * SQL-visible column names (e.g. the call-id column) come from the
 * manifest's `columns` block, never hardcoded (docs/naming.md).
 */

import { BINDING_TYPE_COMPAT } from "@sqlite-host/codegen-core";
import {
  validateScript,
  type BindingValue,
  type Script,
} from "@sqlite-host/runtime-types";
import type {
  HostManifest,
  ManifestMethod,
  ManifestScalarType,
} from "./manifest.js";
import {
  analyzeInsert,
  callIdFilters,
  functionCalls,
  scanNamedParameters,
  tokenizeSql,
  UNKNOWN_ARGS,
} from "./sql.js";

export type LintCode =
  | "invalid-envelope"
  | "duplicate-step-id"
  | "required-api-level-too-high"
  | "method-api-level-too-high"
  | "unknown-required-feature"
  | "unknown-required-method"
  | "duplicate-input-name"
  | "missing-binding"
  | "unused-binding"
  | "binding-type-mismatch"
  | "mixed-prefix-binding"
  | "positional-parameter"
  | "implicit-column-list"
  | "undeclared-method-use"
  | "unused-required-method"
  | "duplicate-call-id"
  | "list-child-later-step"
  | "list-child-without-parent"
  | "undeclared-feature-use"
  | "unknown-function"
  | "function-arity-mismatch"
  | "result-read-unknown-call"
  | "result-read-not-after-call";

export type LintSeverity = "error" | "warning";

/** Feature a script must declare to call inline functions (docs/validation.md). */
const FEATURE_INLINE_FUNCTIONS = "inlineFunctions";

/** An insertable column's scalar type + optionality, for binding-type checks. */
interface InsertableColumn {
  scalarType: ManifestScalarType;
  optional: boolean;
}

/**
 * Binding-type compatibility, single-sourced in ir.ts BINDING_TYPE_COMPAT
 * (docs/proposals/rule-parameters-as-data.md): a null binding is accepted
 * iff the column is optional; otherwise the binding's wire type must be in
 * the column scalar type's accepted set (int64 widens from int32, float64
 * from float32, integers never coerce into float columns).
 */
function bindingCompatible(column: InsertableColumn, bindingType: string): boolean {
  if (bindingType === "null") return column.optional;
  const accepted = BINDING_TYPE_COMPAT[column.scalarType];
  return accepted !== undefined && accepted.includes(bindingType);
}

export interface LintFinding {
  code: LintCode;
  severity: LintSeverity;
  message: string;
  stepId?: string;
  /** Index of the statement within its step; absent for script-level findings. */
  statementIndex?: number;
}

/** A payload is publishable when it has zero errors; warnings don't block. */
export function isPublishable(findings: LintFinding[]): boolean {
  return !findings.some((finding) => finding.severity === "error");
}

interface InsertRecord {
  table: string;
  columns: string[] | null;
  callIds: (string | null)[];
  stepIndex: number;
  stepId: string;
  statementIndex: number;
}

interface ResultReadRecord {
  /** Methods whose result (or result child) tables the statement reads. */
  methods: string[];
  callId: string;
  stepIndex: number;
  stepId: string;
  statementIndex: number;
}

/**
 * Lint a script payload (already JSON-parsed) against a manifest.
 * Returns all findings; order is deterministic (script order).
 */
export function lintScript(payload: unknown, manifest: HostManifest): LintFinding[] {
  const findings: LintFinding[] = [];

  const structural = validateScript(payload);
  for (const finding of structural) {
    findings.push({
      code: finding.code,
      severity: "error",
      message: `${finding.path}: ${finding.message}`,
    });
  }
  if (structural.some((finding) => finding.code === "invalid-envelope")) {
    // The envelope shape can't be trusted; semantic checks would be noise.
    return findings;
  }
  const script = payload as Script;

  // -- structural checks against the manifest ------------------------------
  if (script.requiredApiLevel > manifest.library.apiLevel) {
    findings.push({
      code: "required-api-level-too-high",
      severity: "error",
      message: `requiredApiLevel ${script.requiredApiLevel} exceeds the manifest apiLevel ${manifest.library.apiLevel}`,
    });
  }
  for (const feature of script.requiredFeatures ?? []) {
    if (!manifest.library.features.includes(feature)) {
      findings.push({
        code: "unknown-required-feature",
        severity: "error",
        message: `required feature "${feature}" is not in the manifest features`,
      });
    }
  }
  const methodsByName = new Map(manifest.methods.map((m) => [m.methodName, m]));
  const requiredMethods = script.requiredMethods ?? [];
  for (const method of requiredMethods) {
    if (!methodsByName.has(method)) {
      findings.push({
        code: "unknown-required-method",
        severity: "error",
        message: `required method "${method}" is not defined by the manifest`,
      });
    }
  }
  const seenInputNames = new Set<string>();
  for (const input of script.inputs ?? []) {
    if (seenInputNames.has(input.name)) {
      findings.push({
        code: "duplicate-input-name",
        severity: "error",
        message: `input name "${input.name}" is declared more than once`,
      });
    }
    seenInputNames.add(input.name);
  }

  // -- manifest table index -------------------------------------------------
  const callIdColumn = manifest.columns.callId;
  const callTables = new Map<string, string>(); // call table -> method name
  const inputChildTables = new Map<string, { methodName: string; callTable: string }>();
  const resultTables = new Map<string, string>(); // result table -> method name
  const resultChildTables = new Map<string, string>(); // result child table -> method name
  const inlineFunctions = new Map<string, ManifestMethod>(); // function name (lc) -> method
  // Insertable table (lc) -> column (lc) -> scalar type + optionality, for
  // binding-type checks. Covers the writable tables: call tables (input
  // fields) and input list child tables (item fields).
  const insertableColumns = new Map<string, Map<string, InsertableColumn>>();
  // Tolerate pre-inline manifests (no functionPrefix): an empty prefix
  // never matches, so no identifier is ever flagged unknown-function.
  const functionPrefix = manifest.naming.functionPrefix ?? "";
  const functionPrefixLc = functionPrefix.toLowerCase();
  // SQLite resolves table names case-insensitively, so table maps are
  // keyed lowercased (mirrors the Java engine's ValidationEngine).
  for (const method of manifest.methods) {
    callTables.set(method.callTable.toLowerCase(), method.methodName);
    resultTables.set(method.resultTable.toLowerCase(), method.methodName);
    if (method.inline !== null && method.inline !== undefined) {
      inlineFunctions.set(method.inline.functionName.toLowerCase(), method);
    }
    const callCols = new Map<string, InsertableColumn>();
    for (const field of method.input.fields) {
      callCols.set(field.column.toLowerCase(), {
        scalarType: field.scalarType,
        optional: field.optional,
      });
    }
    insertableColumns.set(method.callTable.toLowerCase(), callCols);
    for (const listField of method.input.listFields) {
      inputChildTables.set(listField.childTable.toLowerCase(), {
        methodName: method.methodName,
        callTable: method.callTable.toLowerCase(),
      });
      const childCols = new Map<string, InsertableColumn>();
      for (const field of listField.itemFields) {
        childCols.set(field.column.toLowerCase(), {
          scalarType: field.scalarType,
          optional: field.optional,
        });
      }
      insertableColumns.set(listField.childTable.toLowerCase(), childCols);
    }
    for (const listField of method.result.listFields) {
      resultChildTables.set(listField.childTable.toLowerCase(), method.methodName);
    }
  }

  // -- per-statement scan ----------------------------------------------------
  const inserts: InsertRecord[] = [];
  const resultReads: ResultReadRecord[] = [];
  const inlineInvokedMethods = new Set<string>(); // methods invoked via inline function
  script.steps.forEach((step, stepIndex) => {
    step.statements.forEach((statement, statementIndex) => {
      const at = { stepId: step.id, statementIndex };
      const bindings: Record<string, BindingValue> = statement.bindings ?? {};
      const bindingNames = Object.keys(bindings);
      const parameters = scanNamedParameters(statement.sql);
      for (const parameter of parameters) {
        if (!bindingNames.includes(parameter)) {
          findings.push({
            code: "missing-binding",
            severity: "error",
            message: `SQL references parameter "${parameter}" with no binding`,
            ...at,
          });
        }
      }
      for (const name of bindingNames) {
        if (!parameters.includes(name)) {
          findings.push({
            code: "unused-binding",
            severity: "error",
            message: `binding "${name}" is not referenced by the SQL`,
            ...at,
          });
        }
      }

      const tokens = tokenizeSql(statement.sql);

      // Positional "?" / "?N" placeholders are forbidden by protocol v1
      // (docs/script-envelope.md — named parameters only); the shared
      // scanner lexes them as "?" punctuation, so one finding per
      // statement on the first occurrence (mirrors the Java engine).
      if (tokens.some((token) => token.kind === "punct" && token.value === "?")) {
        findings.push({
          code: "positional-parameter",
          severity: "error",
          message: `SQL uses a positional parameter "?" — positional parameters are not supported in v1; use a named parameter (":name")`,
          ...at,
        });
      }

      // Same bare name through more than one prefix form in one
      // statement: supported by the runtime, but usually an accident.
      const prefixesByName = new Map<string, Set<string>>();
      for (const token of tokens) {
        if (token.kind !== "parameter" || token.prefix === undefined) continue;
        let prefixes = prefixesByName.get(token.value);
        if (prefixes === undefined) {
          prefixes = new Set();
          prefixesByName.set(token.value, prefixes);
        }
        prefixes.add(token.prefix);
      }
      for (const [name, prefixes] of prefixesByName) {
        if (prefixes.size > 1) {
          findings.push({
            code: "mixed-prefix-binding",
            severity: "warning",
            message: `parameter "${name}" is referenced through multiple prefix forms (${[...prefixes]
              .map((prefix) => `${prefix}${name}`)
              .join(", ")}) in one statement — use :${name} consistently`,
            ...at,
          });
        }
      }

      const insert = analyzeInsert(tokens, bindings, callIdColumn);
      if (insert !== null) {
        inserts.push({
          table: insert.table,
          columns: insert.columns,
          callIds: insert.rows.map((row) => row.callId),
          stepIndex,
          stepId: step.id,
          statementIndex,
        });

        // binding-type-mismatch: for a write into a call table (or input
        // list child table) with an explicit column list, a parameter that
        // feeds a known column must be type-compatible with the column's
        // scalar type (docs/validation.md). Mirrors the Java engine over
        // the shared BINDING_TYPE_COMPAT table.
        const columnTypes = insertableColumns.get(insert.table);
        if (columnTypes !== undefined) {
          for (const row of insert.rows) {
            for (const cell of row.cells) {
              const columnType = columnTypes.get(cell.column);
              const binding = bindings[cell.param];
              if (columnType === undefined || binding === undefined) {
                continue; // unknown column / missing-binding reported elsewhere
              }
              if (!bindingCompatible(columnType, binding.type)) {
                findings.push({
                  code: "binding-type-mismatch",
                  severity: "error",
                  message: `binding "${cell.param}" of type ${binding.type} is not compatible with column ${insert.table}.${cell.column} (${columnType.scalarType}${columnType.optional ? ", optional" : ""})`,
                  ...at,
                });
              }
            }
          }
        }
      }

      // Inline function lint (docs/validation.md — feature
      // inlineFunctions) over the identifier(...) calls: a manifest
      // inline function must be declared through requiredFeatures
      // (undeclared-feature-use) and called with an argument count
      // inside minArgs..maxArgs (function-arity-mismatch); an unmatched
      // identifier is unknown-function only when it carries the host's
      // functionPrefix — non-prefix identifiers (max(...), abs(...))
      // are SQLite's business, not the lint's (mirrors the Java engine).
      const reportedFunctions = new Set<string>();
      for (const call of functionCalls(tokens)) {
        const nameLc = call.name.toLowerCase();
        const method = inlineFunctions.get(nameLc);
        if (method === undefined) {
          if (
            functionPrefixLc !== "" &&
            nameLc.startsWith(functionPrefixLc) &&
            !reportedFunctions.has(nameLc)
          ) {
            reportedFunctions.add(nameLc);
            findings.push({
              code: "unknown-function",
              severity: "error",
              message: `function "${call.name}" matches the functionPrefix "${functionPrefix}" but is not an inline function of the manifest`,
              ...at,
            });
          }
          continue;
        }
        inlineInvokedMethods.add(method.methodName);
        const inline = method.inline!;
        if (
          !(script.requiredFeatures ?? []).includes(FEATURE_INLINE_FUNCTIONS) &&
          !reportedFunctions.has(nameLc)
        ) {
          reportedFunctions.add(nameLc);
          findings.push({
            code: "undeclared-feature-use",
            severity: "error",
            message: `inline function "${call.name}" requires feature "${FEATURE_INLINE_FUNCTIONS}" which is not declared in requiredFeatures`,
            ...at,
          });
        }
        // Inline invocation is not gated by requiredMethods, so the
        // apiLevel dependency must be checked here too: a level-1 host
        // that supports inlineFunctions but lacks this method would raise
        // "no such function" at runtime instead of clean-skipping.
        if (method.apiLevel > script.requiredApiLevel && !reportedFunctions.has(nameLc)) {
          reportedFunctions.add(nameLc);
          findings.push({
            code: "method-api-level-too-high",
            severity: "error",
            message: `inline function "${call.name}" (method "${method.methodName}") requires apiLevel ${method.apiLevel} which exceeds the script's requiredApiLevel ${script.requiredApiLevel}`,
            ...at,
          });
        }
        if (
          call.argCount !== UNKNOWN_ARGS &&
          (call.argCount < inline.minArgs || call.argCount > inline.maxArgs)
        ) {
          findings.push({
            code: "function-arity-mismatch",
            severity: "error",
            message: `inline function "${call.name}" is called with ${call.argCount} argument(s) but takes ${inline.minArgs}${inline.maxArgs === inline.minArgs ? "" : `..${inline.maxArgs}`}`,
            ...at,
          });
        }
      }

      // Result-read lineage collection: result tables referenced +
      // statically resolvable call-id filters (mirrors the Java engine).
      const readMethods: string[] = [];
      for (const token of tokens) {
        if (token.kind !== "identifier" && token.kind !== "quoted-identifier") continue;
        const table = token.value.toLowerCase();
        const method = resultTables.get(table) ?? resultChildTables.get(table);
        if (method !== undefined && !readMethods.includes(method)) {
          readMethods.push(method);
        }
      }
      if (readMethods.length > 0) {
        for (const callId of callIdFilters(tokens, bindings, callIdColumn)) {
          resultReads.push({
            methods: readMethods,
            callId,
            stepIndex,
            stepId: step.id,
            statementIndex,
          });
        }
      }
    });
  });

  // -- host-call usage checks -------------------------------------------------
  // Keyed by call id alone: the queue table declares call_id UNIQUE and
  // every call-table trigger inserts into it, so call ids are global
  // across call tables — not per table.
  const seenCallIds = new Map<string, InsertRecord>();
  for (const insert of inserts) {
    const at = { stepId: insert.stepId, statementIndex: insert.statementIndex };
    const isCallTable = callTables.has(insert.table);
    const isChildTable =
      inputChildTables.has(insert.table) || resultChildTables.has(insert.table);

    if ((isCallTable || isChildTable) && insert.columns === null) {
      findings.push({
        code: "implicit-column-list",
        severity: "error",
        message: `INSERT INTO ${insert.table} must use an explicit column list`,
        ...at,
      });
    }

    if (isCallTable) {
      const methodName = callTables.get(insert.table)!;
      if (!requiredMethods.includes(methodName)) {
        findings.push({
          code: "undeclared-method-use",
          severity: "error",
          message: `INSERT INTO ${insert.table} uses method "${methodName}" which is not in requiredMethods`,
          ...at,
        });
      }
      // The script depends on this method's apiLevel: an under-declared
      // requiredApiLevel would let an older host silently fail to
      // clean-skip (mirrors required-api-level-too-high, but per method).
      const method = methodsByName.get(methodName);
      if (method !== undefined && method.apiLevel > script.requiredApiLevel) {
        findings.push({
          code: "method-api-level-too-high",
          severity: "error",
          message: `method "${methodName}" requires apiLevel ${method.apiLevel} which exceeds the script's requiredApiLevel ${script.requiredApiLevel}`,
          ...at,
        });
      }
      for (const callId of insert.callIds) {
        if (callId === null) continue;
        const first = seenCallIds.get(callId);
        if (first !== undefined) {
          findings.push({
            code: "duplicate-call-id",
            severity: "error",
            message: `${callIdColumn} "${callId}" is emitted more than once (call ids are unique across all call tables; first emitted into ${first.table})`,
            ...at,
          });
        } else {
          seenCallIds.set(callId, insert);
        }
      }
    }
  }

  // -- list parent/child colocation -------------------------------------------
  for (const insert of inserts) {
    const child = inputChildTables.get(insert.table);
    if (child === undefined) continue;
    const at = { stepId: insert.stepId, statementIndex: insert.statementIndex };
    const checkedIds = new Set<string>();
    for (const callId of insert.callIds) {
      if (callId === null || checkedIds.has(callId)) continue;
      checkedIds.add(callId);
      const parent = inserts.find(
        (candidate) =>
          candidate.table === child.callTable && candidate.callIds.includes(callId),
      );
      if (parent !== undefined) {
        if (parent.stepIndex !== insert.stepIndex) {
          findings.push({
            code: "list-child-later-step",
            severity: "error",
            message: `child rows in ${insert.table} for ${callIdColumn} "${callId}" must be emitted in the same step as the parent call row in ${child.callTable}`,
            ...at,
          });
        }
        continue;
      }
      // Best-effort guard (mirrors the Java engine): a parent insert
      // with a computed (unresolvable) call-id could produce this id.
      const methodHasComputedEmit = inserts.some(
        (candidate) =>
          candidate.table === child.callTable && candidate.callIds.includes(null),
      );
      if (!methodHasComputedEmit) {
        findings.push({
          code: "list-child-without-parent",
          severity: "error",
          message: `child rows in ${insert.table} reference ${callIdColumn} "${callId}" but no statement inserts that call into ${child.callTable}`,
          ...at,
        });
      }
    }
  }

  // -- unused required methods --------------------------------------------------
  for (const methodName of requiredMethods) {
    const method = methodsByName.get(methodName);
    if (method === undefined) continue; // already unknown-required-method
    if (
      !inserts.some((insert) => insert.table === method.callTable.toLowerCase()) &&
      !inlineInvokedMethods.has(methodName)
    ) {
      findings.push({
        code: "unused-required-method",
        severity: "warning",
        message: `required method "${methodName}" is never called (no INSERT INTO ${method.callTable} and no inline function invocation)`,
      });
    }
  }

  // -- result-read lineage --------------------------------------------------
  // method -> statically emitted call-id -> earliest emitting step.
  const staticEmits = new Map<string, Map<string, number>>();
  // method -> earliest step with a computed (unresolvable) emit.
  const computedEmits = new Map<string, number>();
  for (const insert of inserts) {
    const methodName = callTables.get(insert.table);
    if (methodName === undefined) continue;
    // A call-table insert with no resolvable rows (e.g. DEFAULT VALUES)
    // still counts as a write with an unresolvable id.
    const callIds = insert.callIds.length > 0 ? insert.callIds : [null];
    for (const callId of callIds) {
      if (callId === null) {
        const step = computedEmits.get(methodName);
        computedEmits.set(
          methodName,
          step === undefined ? insert.stepIndex : Math.min(step, insert.stepIndex),
        );
      } else {
        let byId = staticEmits.get(methodName);
        if (byId === undefined) {
          byId = new Map();
          staticEmits.set(methodName, byId);
        }
        const step = byId.get(callId);
        byId.set(callId, step === undefined ? insert.stepIndex : Math.min(step, insert.stepIndex));
      }
    }
  }
  // A statement can join result tables of several methods (set M) while
  // each resolved call-id belongs to only one of them, so a finding is
  // reported only when NO method in M can satisfy the read: unknown-call
  // when no method emits the id (computed emits count as possible
  // matches — skip), not-after-call when every emitting method violates
  // the strictly-later ordering (mirrors the Java engine).
  for (const read of resultReads) {
    const at = { stepId: read.stepId, statementIndex: read.statementIndex };
    let satisfied = false;
    const unknownMethods: string[] = [];
    const notAfterMethods: string[] = [];
    for (const method of read.methods) {
      const emitStep = staticEmits.get(method)?.get(read.callId);
      const computedStep = computedEmits.get(method);
      if (emitStep === undefined) {
        // Best-effort: a computed emit for this method could produce
        // the id — skip rather than false-positive.
        if (computedStep !== undefined) {
          satisfied = true;
        } else {
          unknownMethods.push(method);
        }
        continue;
      }
      const earlierComputed = computedStep !== undefined && computedStep < read.stepIndex;
      if (emitStep < read.stepIndex || earlierComputed) {
        satisfied = true;
      } else {
        notAfterMethods.push(method);
      }
    }
    if (satisfied) continue;
    if (notAfterMethods.length > 0) {
      for (const method of notAfterMethods) {
        findings.push({
          code: "result-read-not-after-call",
          severity: "error",
          message: `statement reads results of method "${method}" for ${callIdColumn} "${read.callId}" in the same or an earlier step than the emitting insert — results only exist after the emitting step's drain`,
          ...at,
        });
      }
    } else {
      for (const method of unknownMethods) {
        findings.push({
          code: "result-read-unknown-call",
          severity: "error",
          message: `statement reads results of method "${method}" for ${callIdColumn} "${read.callId}" but no statement emits that call`,
          ...at,
        });
      }
    }
  }

  return findings;
}
