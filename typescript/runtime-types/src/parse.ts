/**
 * Handwritten structural validation and parsing for the script
 * envelope. Codes match docs/validation.md: `invalid-envelope` for
 * missing/empty/mistyped required envelope fields, `duplicate-step-id`
 * for repeated step ids. This is structural validation only — semantic
 * lint (manifest-aware checks) lives in @sqlite-host/authoring.
 */

import {
  BINDING_TYPES,
  SCRIPT_ENGINE_V1,
  type BindingType,
  type Script,
} from "./generated/envelope.js";
import {
  INT32_MAX,
  INT32_MIN,
  int64ToBigInt,
  isValidBase64,
} from "./bindings.js";

export type EnvelopeFindingCode = "invalid-envelope" | "duplicate-step-id";

export interface EnvelopeFinding {
  code: EnvelopeFindingCode;
  /** JSON-path-like location, e.g. `steps[0].statements[1].sql`. */
  path: string;
  message: string;
}

function invalid(path: string, message: string): EnvelopeFinding {
  return { code: "invalid-envelope", path, message };
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

const DECIMAL_STRING = /^-?[0-9]+$/;

/** Validate one binding value node; returns findings (empty = valid). */
export function validateBindingValue(value: unknown, path: string): EnvelopeFinding[] {
  if (!isPlainObject(value)) {
    return [invalid(path, "binding value must be an object")];
  }
  const type = value["type"];
  if (typeof type !== "string" || !(BINDING_TYPES as readonly string[]).includes(type)) {
    return [
      invalid(`${path}.type`, `binding type must be one of ${BINDING_TYPES.join(", ")}`),
    ];
  }
  const v = value["value"];
  switch (type as BindingType) {
    case "null":
      if ("value" in value) {
        return [invalid(`${path}.value`, "null bindings must not carry a value")];
      }
      return [];
    case "int32": {
      if (typeof v === "number") {
        if (!Number.isInteger(v) || v < INT32_MIN || v > INT32_MAX) {
          return [invalid(`${path}.value`, "int32 value must be an integer in int32 range")];
        }
        return [];
      }
      if (typeof v === "string") {
        if (!DECIMAL_STRING.test(v) || Number(v) < INT32_MIN || Number(v) > INT32_MAX) {
          return [invalid(`${path}.value`, "int32 string value must be decimal in int32 range")];
        }
        return [];
      }
      return [invalid(`${path}.value`, "int32 value must be a number or decimal string")];
    }
    case "int64": {
      if (typeof v !== "number" && typeof v !== "string") {
        return [invalid(`${path}.value`, "int64 value must be a number or decimal string")];
      }
      try {
        int64ToBigInt(v);
      } catch (error) {
        return [invalid(`${path}.value`, (error as Error).message)];
      }
      return [];
    }
    case "bool":
      if (typeof v !== "boolean") {
        return [invalid(`${path}.value`, "bool value must be true or false")];
      }
      return [];
    case "text":
      if (typeof v !== "string") {
        return [invalid(`${path}.value`, "text value must be a string")];
      }
      return [];
    case "blob":
      if (typeof v !== "string" || !isValidBase64(v)) {
        return [
          invalid(
            `${path}.value`,
            "blob value must be standard base64 (padding, no line breaks)",
          ),
        ];
      }
      return [];
    case "float32":
      // Floats have no string form (docs/script-envelope.md); the number
      // must survive round-to-nearest-single without overflowing.
      if (typeof v !== "number" || !Number.isFinite(v)) {
        return [invalid(`${path}.value`, "float32 value must be a finite JSON number")];
      }
      if (!Number.isFinite(Math.fround(v))) {
        return [
          invalid(
            `${path}.value`,
            "float32 value must remain finite after rounding to an IEEE-754 single",
          ),
        ];
      }
      return [];
    case "float64":
      if (typeof v !== "number" || !Number.isFinite(v)) {
        return [invalid(`${path}.value`, "float64 value must be a finite JSON number")];
      }
      return [];
  }
}

/** Validate one runtime input node. */
export function validateRuntimeInput(value: unknown, path: string): EnvelopeFinding[] {
  if (!isPlainObject(value)) {
    return [invalid(path, "input must be an object")];
  }
  const findings: EnvelopeFinding[] = [];
  if (typeof value["name"] !== "string" || value["name"] === "") {
    findings.push(invalid(`${path}.name`, "input name must be a non-empty string"));
  }
  findings.push(...validateBindingValue(value["value"], `${path}.value`));
  return findings;
}

/** Validate one statement node. */
export function validateStatement(value: unknown, path: string): EnvelopeFinding[] {
  if (!isPlainObject(value)) {
    return [invalid(path, "statement must be an object")];
  }
  const findings: EnvelopeFinding[] = [];
  if (typeof value["sql"] !== "string" || value["sql"] === "") {
    findings.push(invalid(`${path}.sql`, "statement sql must be a non-empty string"));
  }
  const bindings = value["bindings"];
  if (bindings !== undefined) {
    if (!isPlainObject(bindings)) {
      findings.push(invalid(`${path}.bindings`, "bindings must be an object map"));
    } else {
      for (const [name, binding] of Object.entries(bindings)) {
        if (name === "") {
          findings.push(invalid(`${path}.bindings`, "binding names must be non-empty"));
        }
        findings.push(...validateBindingValue(binding, `${path}.bindings.${name}`));
      }
    }
  }
  return findings;
}

/** Validate one step node (statements included; id uniqueness is script-level). */
export function validateStep(value: unknown, path: string): EnvelopeFinding[] {
  if (!isPlainObject(value)) {
    return [invalid(path, "step must be an object")];
  }
  const findings: EnvelopeFinding[] = [];
  if (typeof value["id"] !== "string" || value["id"] === "") {
    findings.push(invalid(`${path}.id`, "step id must be a non-empty string"));
  }
  const statements = value["statements"];
  if (!Array.isArray(statements) || statements.length === 0) {
    findings.push(invalid(`${path}.statements`, "step statements must be a non-empty array"));
  } else {
    statements.forEach((statement, index) => {
      findings.push(...validateStatement(statement, `${path}.statements[${index}]`));
    });
  }
  return findings;
}

function validateStringArray(
  value: unknown,
  path: string,
  findings: EnvelopeFinding[],
): void {
  if (!Array.isArray(value)) {
    findings.push(invalid(path, "must be an array of strings"));
    return;
  }
  value.forEach((entry, index) => {
    if (typeof entry !== "string" || entry === "") {
      findings.push(invalid(`${path}[${index}]`, "must be a non-empty string"));
    }
  });
}

/**
 * Structurally validate a parsed JSON value against the envelope
 * contract. Returns findings; an empty array means the value is a
 * well-formed Script.
 */
export function validateScript(value: unknown): EnvelopeFinding[] {
  if (!isPlainObject(value)) {
    return [invalid("$", "script must be a JSON object")];
  }
  const findings: EnvelopeFinding[] = [];

  if (value["engine"] !== SCRIPT_ENGINE_V1) {
    findings.push(invalid("engine", `engine must be the string "${SCRIPT_ENGINE_V1}"`));
  }
  if (value["scriptId"] !== undefined && typeof value["scriptId"] !== "string") {
    findings.push(invalid("scriptId", "scriptId must be a string when present"));
  }
  const apiLevel = value["requiredApiLevel"];
  if (typeof apiLevel !== "number" || !Number.isInteger(apiLevel) || apiLevel < 1) {
    findings.push(invalid("requiredApiLevel", "requiredApiLevel must be an integer >= 1"));
  }
  if (value["requiredFeatures"] !== undefined) {
    validateStringArray(value["requiredFeatures"], "requiredFeatures", findings);
  }
  if (value["requiredMethods"] !== undefined) {
    validateStringArray(value["requiredMethods"], "requiredMethods", findings);
  }
  const inputs = value["inputs"];
  if (inputs !== undefined) {
    if (!Array.isArray(inputs)) {
      findings.push(invalid("inputs", "inputs must be an array"));
    } else {
      inputs.forEach((input, index) => {
        findings.push(...validateRuntimeInput(input, `inputs[${index}]`));
      });
    }
  }

  const steps = value["steps"];
  if (!Array.isArray(steps) || steps.length === 0) {
    findings.push(invalid("steps", "steps must be a non-empty array"));
    return findings;
  }
  const seenIds = new Set<string>();
  steps.forEach((step, index) => {
    findings.push(...validateStep(step, `steps[${index}]`));
    if (isPlainObject(step) && typeof step["id"] === "string" && step["id"] !== "") {
      const id = step["id"];
      if (seenIds.has(id)) {
        findings.push({
          code: "duplicate-step-id",
          path: `steps[${index}].id`,
          message: `step id "${id}" is used more than once`,
        });
      }
      seenIds.add(id);
    }
  });
  return findings;
}

/** Thrown by parseScript when the payload fails structural validation. */
export class ScriptParseError extends Error {
  readonly findings: EnvelopeFinding[];

  constructor(findings: EnvelopeFinding[]) {
    super(
      `invalid script envelope: ${findings
        .map((f) => `${f.code} at ${f.path}: ${f.message}`)
        .join("; ")}`,
    );
    this.name = "ScriptParseError";
    this.findings = findings;
  }
}

/**
 * Parse a script envelope from JSON text with structural validation.
 * Throws ScriptParseError (carrying `invalid-envelope` /
 * `duplicate-step-id` findings) when the payload is malformed, and
 * SyntaxError when the text is not JSON at all.
 */
export function parseScript(json: string): Script {
  const value: unknown = JSON.parse(json);
  const findings = validateScript(value);
  if (findings.length > 0) {
    throw new ScriptParseError(findings);
  }
  return value as Script;
}
