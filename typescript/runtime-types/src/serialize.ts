/**
 * Canonical script envelope serialization: pinned key order (as in
 * docs/script-envelope.md), 2-space indentation, LF, trailing newline.
 * Parsing a canonical payload and serializing it again reproduces the
 * original bytes (fixtures/payloads/valid are the golden inputs).
 */

import type {
  BindingValue,
  RuntimeInput,
  Script,
  Statement,
  Step,
} from "./generated/envelope.js";

function bindingValueJson(value: BindingValue): object {
  if (value.type === "null") {
    return { type: value.type };
  }
  return { type: value.type, value: value.value };
}

function bindingsJson(bindings: Record<string, BindingValue>): object {
  const out: Record<string, object> = {};
  for (const [name, value] of Object.entries(bindings)) {
    out[name] = bindingValueJson(value);
  }
  return out;
}

function statementJson(statement: Statement): object {
  return {
    sql: statement.sql,
    ...(statement.bindings !== undefined
      ? { bindings: bindingsJson(statement.bindings) }
      : {}),
  };
}

function stepJson(step: Step): object {
  return {
    id: step.id,
    statements: step.statements.map(statementJson),
  };
}

function inputJson(input: RuntimeInput): object {
  return {
    name: input.name,
    value: bindingValueJson(input.value),
  };
}

/** Serialize a script to canonical JSON bytes (2-space, trailing LF). */
export function serializeScript(script: Script): string {
  const json = {
    engine: script.engine,
    ...(script.scriptId !== undefined ? { scriptId: script.scriptId } : {}),
    requiredApiLevel: script.requiredApiLevel,
    ...(script.requiredFeatures !== undefined
      ? { requiredFeatures: script.requiredFeatures }
      : {}),
    ...(script.requiredMethods !== undefined
      ? { requiredMethods: script.requiredMethods }
      : {}),
    ...(script.inputs !== undefined ? { inputs: script.inputs.map(inputJson) } : {}),
    steps: script.steps.map(stepJson),
  };
  return JSON.stringify(json, null, 2) + "\n";
}
