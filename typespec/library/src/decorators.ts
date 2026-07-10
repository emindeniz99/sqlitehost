import type {
  DecoratorContext,
  Interface,
  ModelProperty,
  Operation,
  Program,
} from "@typespec/compiler";
import { reportDiagnostic, stateKeys } from "./lib.js";

/** Resolved `@hostLibrary` options (naming keys stay optional here; the frontend applies defaults). */
export interface HostLibraryOptions {
  apiLevel: number;
  callTablePrefix?: string;
  resultTablePrefix?: string;
  inputColumnPrefix?: string;
  resultColumnPrefix?: string;
  inputListTableInfix?: string;
  resultListTableInfix?: string;
}

/** Resolved `@hostMethod` options. `apiLevel` defaults to the library api level in the frontend. */
export interface HostMethodOptions {
  name: string;
  handler: string;
  apiLevel?: number;
}

const IDENTIFIER = /^[A-Za-z_][A-Za-z0-9_]*$/;
const METHOD_NAME = /^[A-Za-z][A-Za-z0-9_]*$/;
const SQL_NAME = /^[a-z][a-z0-9_]*$/;

/**
 * Normalize a decorator argument to a plain JS value. The options
 * decorators take model-typed arguments, so `{ apiLevel: 1, ... }`
 * arrives as a model expression whose property types are literal
 * types; `valueof` arguments (e.g. @sqlName) arrive as JS primitives
 * already.
 */
function unwrap(value: unknown): unknown {
  if (value !== null && typeof value === "object" && "kind" in value) {
    const t = value as {
      kind: string;
      properties?: Map<string, { type: unknown }>;
      values?: unknown[];
      value?: unknown;
    };
    switch (t.kind) {
      case "Model": {
        const out: Record<string, unknown> = {};
        for (const [name, prop] of t.properties ?? []) {
          out[name] = unwrap(prop.type);
        }
        return out;
      }
      case "Tuple":
        return (t.values ?? []).map(unwrap);
      case "String":
      case "Number":
      case "Boolean":
        return t.value;
      default:
        return value;
    }
  }
  return value;
}

function checkApiLevel(
  context: DecoratorContext,
  apiLevel: number | undefined,
): void {
  if (apiLevel !== undefined && (!Number.isInteger(apiLevel) || apiLevel < 1)) {
    reportDiagnostic(context.program, {
      code: "invalid-api-level",
      format: { value: String(apiLevel) },
      target: context.decoratorTarget,
    });
  }
}

export function $hostLibrary(
  context: DecoratorContext,
  target: Interface,
  options: unknown,
): void {
  const opts = unwrap(options) as HostLibraryOptions;
  checkApiLevel(context, opts.apiLevel);
  context.program.stateMap(stateKeys.hostLibrary).set(target, opts);
}

export function $hostMethod(
  context: DecoratorContext,
  target: Operation,
  options: unknown,
): void {
  const opts = unwrap(options) as HostMethodOptions;
  if (!METHOD_NAME.test(opts.name)) {
    reportDiagnostic(context.program, {
      code: "invalid-method-name",
      format: { name: opts.name },
      target: context.decoratorTarget,
    });
  }
  if (!IDENTIFIER.test(opts.handler)) {
    reportDiagnostic(context.program, {
      code: "invalid-handler-name",
      format: { name: opts.handler },
      target: context.decoratorTarget,
    });
  }
  checkApiLevel(context, opts.apiLevel);
  context.program.stateMap(stateKeys.hostMethod).set(target, opts);
}

export function $sqlName(
  context: DecoratorContext,
  target: ModelProperty,
  name: unknown,
): void {
  const value = unwrap(name) as string;
  if (!SQL_NAME.test(value)) {
    reportDiagnostic(context.program, {
      code: "invalid-sql-name",
      format: { name: value },
      target: context.decoratorTarget,
    });
  }
  context.program.stateMap(stateKeys.sqlName).set(target, value);
}

/** Options recorded by `@hostLibrary`, or undefined when absent. */
export function getHostLibraryOptions(
  program: Program,
  target: Interface,
): HostLibraryOptions | undefined {
  return program.stateMap(stateKeys.hostLibrary).get(target) as
    | HostLibraryOptions
    | undefined;
}

/** All interfaces decorated with `@hostLibrary`, in decorator execution order. */
export function getHostLibraryInterfaces(program: Program): Interface[] {
  return [...program.stateMap(stateKeys.hostLibrary).keys()] as Interface[];
}

/** Options recorded by `@hostMethod`, or undefined when absent. */
export function getHostMethodOptions(
  program: Program,
  target: Operation,
): HostMethodOptions | undefined {
  return program.stateMap(stateKeys.hostMethod).get(target) as
    | HostMethodOptions
    | undefined;
}

/** SQL name override recorded by `@sqlName`, or undefined when absent. */
export function getSqlName(
  program: Program,
  target: ModelProperty,
): string | undefined {
  return program.stateMap(stateKeys.sqlName).get(target) as string | undefined;
}
