/**
 * Canonical script envelope serialization: pinned key order (as in
 * docs/script-envelope.md), 2-space indentation, LF, trailing newline.
 * Parsing a canonical payload and serializing it again reproduces the
 * original bytes whenever every float32 in it is already representable
 * as an IEEE-754 single (fixtures/payloads/valid are the golden inputs).
 *
 * That qualifier is the float32 contract, not a weakness in this file.
 * `float32` is round-to-nearest on parse (docs/script-envelope.md), so a
 * value the wire spells 0.1 is the number 0.10000000149011612 by the
 * time it reaches here, and re-serializing writes what the engine will
 * actually store. Round-tripping the wider double instead would keep the
 * bytes and lose the agreement with the Java and C# readers, which is
 * the property that matters. A second pass is byte-stable for every
 * payload: rounding a single-representable value is the identity.
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
