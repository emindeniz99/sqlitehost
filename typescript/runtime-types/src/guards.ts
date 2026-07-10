/**
 * Type guards over the generated envelope types, backed by the same
 * structural validators used by parseScript.
 */

import type {
  BindingValue,
  RuntimeInput,
  Script,
  Statement,
  Step,
} from "./generated/envelope.js";
import {
  validateBindingValue,
  validateRuntimeInput,
  validateScript,
  validateStatement,
  validateStep,
} from "./parse.js";

export function isBindingValue(value: unknown): value is BindingValue {
  return validateBindingValue(value, "$").length === 0;
}

export function isRuntimeInput(value: unknown): value is RuntimeInput {
  return validateRuntimeInput(value, "$").length === 0;
}

export function isStatement(value: unknown): value is Statement {
  return validateStatement(value, "$").length === 0;
}

export function isStep(value: unknown): value is Step {
  return validateStep(value, "$").length === 0;
}

export function isScript(value: unknown): value is Script {
  return validateScript(value).length === 0;
}
