/**
 * Host-level naming conventions and derivation rules. This module is the
 * single implementation of name derivation; emitters must never derive
 * table/column names themselves — the frontend resolves them into the IR
 * using these functions, and every emitter reads the resolved names.
 */

import type { NamingIr } from "./ir.js";

export const DEFAULT_NAMING: NamingIr = {
  callTablePrefix: "call_",
  resultTablePrefix: "result_",
  inputColumnPrefix: "input_",
  resultColumnPrefix: "result_",
  inputListTableInfix: "__input_",
  resultListTableInfix: "__result_",
};

/**
 * Convert a camelCase / PascalCase identifier to snake_case.
 *
 * Rule: insert "_" before an uppercase letter when it follows a lowercase
 * letter or digit, or when it is followed by a lowercase letter; then
 * lowercase everything. Examples:
 *   getValue     -> get_value
 *   defaultValue -> default_value
 *   HTTPServer   -> http_server
 *   putBlob2X    -> put_blob2_x
 */
export function toSnakeCase(name: string): string {
  let out = "";
  for (let i = 0; i < name.length; i++) {
    const ch = name[i];
    if (ch >= "A" && ch <= "Z") {
      const prev = i > 0 ? name[i - 1] : "";
      const next = i + 1 < name.length ? name[i + 1] : "";
      const prevIsLowerOrDigit =
        (prev >= "a" && prev <= "z") || (prev >= "0" && prev <= "9");
      const nextIsLower = next >= "a" && next <= "z";
      if (i > 0 && (prevIsLowerOrDigit || nextIsLower)) {
        out += "_";
      }
      out += ch.toLowerCase();
    } else {
      out += ch;
    }
  }
  return out;
}

/**
 * Convert a camelCase / PascalCase identifier to kebab-case (the
 * snake_case rule with "-" separators). Used to derive per-library
 * artifact base names from @hostLibrary interface names, e.g.
 * GameHostMethods -> game-host-methods.
 */
export function toKebabCase(name: string): string {
  return toSnakeCase(name).replace(/_/g, "-");
}

export function deriveCallTable(naming: NamingIr, methodName: string): string {
  return naming.callTablePrefix + toSnakeCase(methodName);
}

export function deriveResultTable(naming: NamingIr, methodName: string): string {
  return naming.resultTablePrefix + toSnakeCase(methodName);
}

export function deriveInputColumn(naming: NamingIr, sqlName: string): string {
  return naming.inputColumnPrefix + sqlName;
}

export function deriveResultColumn(naming: NamingIr, sqlName: string): string {
  return naming.resultColumnPrefix + sqlName;
}

export function deriveInputListTable(
  naming: NamingIr,
  methodName: string,
  sqlName: string,
): string {
  return deriveCallTable(naming, methodName) + naming.inputListTableInfix + sqlName;
}

export function deriveResultListTable(
  naming: NamingIr,
  methodName: string,
  sqlName: string,
): string {
  return deriveResultTable(naming, methodName) + naming.resultListTableInfix + sqlName;
}

export function deriveQueueTrigger(naming: NamingIr, methodName: string): string {
  return "trg_" + deriveCallTable(naming, methodName) + "_queue";
}
