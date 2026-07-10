/**
 * Fluent script builder. build() validates the assembled envelope
 * structurally (same rules as parseScript) and returns a Script whose
 * serializeScript() output is canonical payload bytes.
 */

import {
  SCRIPT_ENGINE_V1,
  ScriptParseError,
  validateScript,
  type BindingValue,
  type RuntimeInput,
  type Script,
  type Statement,
  type Step,
} from "@sqlite-host/runtime-types";

export interface ScriptOptions {
  /** Defaults to "sqlite-host-v1". */
  engine?: string;
  scriptId?: string;
  requiredApiLevel: number;
  requiredFeatures?: string[];
  requiredMethods?: string[];
  inputs?: RuntimeInput[];
}

export class StepBuilder {
  private readonly script: ScriptBuilder;
  private readonly current: Step;

  constructor(script: ScriptBuilder, step: Step) {
    this.script = script;
    this.current = step;
  }

  /** Append a statement to this step. Omitting bindings omits the key. */
  statement(sql: string, bindings?: Record<string, BindingValue>): StepBuilder {
    const statement: Statement = bindings === undefined ? { sql } : { sql, bindings };
    this.current.statements.push(statement);
    return this;
  }

  /** Start the next step. */
  step(id: string): StepBuilder {
    return this.script.step(id);
  }

  /** Finish the script (validates the envelope). */
  build(): Script {
    return this.script.build();
  }
}

export class ScriptBuilder {
  private readonly draft: Script;

  constructor(options: ScriptOptions) {
    this.draft = {
      engine: options.engine ?? SCRIPT_ENGINE_V1,
      ...(options.scriptId !== undefined ? { scriptId: options.scriptId } : {}),
      requiredApiLevel: options.requiredApiLevel,
      ...(options.requiredFeatures !== undefined
        ? { requiredFeatures: options.requiredFeatures }
        : {}),
      ...(options.requiredMethods !== undefined
        ? { requiredMethods: options.requiredMethods }
        : {}),
      ...(options.inputs !== undefined ? { inputs: options.inputs } : {}),
      steps: [],
    };
  }

  /** Start a new step with the given id. */
  step(id: string): StepBuilder {
    const step: Step = { id, statements: [] };
    this.draft.steps.push(step);
    return new StepBuilder(this, step);
  }

  /**
   * Validate the assembled envelope and return it. Throws
   * ScriptParseError when the script is structurally invalid (e.g. no
   * steps, duplicate step ids, malformed binding values).
   */
  build(): Script {
    const findings = validateScript(this.draft);
    if (findings.length > 0) {
      throw new ScriptParseError(findings);
    }
    return this.draft;
  }
}

/** Entry point: `script({...}).step("id").statement(sql, {...}).build()`. */
export function script(options: ScriptOptions): ScriptBuilder {
  return new ScriptBuilder(options);
}
