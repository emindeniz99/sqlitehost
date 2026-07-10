/**
 * Lexical SQL scanning for the static authoring lint. The named
 * parameter scanner implements the shared algorithm from
 * docs/errors.md: scan for `:name` / `@name` / `$name` while skipping
 * string literals ('…' with '' escapes), double-quoted identifiers,
 * line comments (--) and block comments. The same algorithm is used by
 * the C# runtime and the Java validator.
 *
 * The INSERT analysis on top of the token stream is deliberately
 * best-effort (docs/validation.md): it resolves statically-known
 * call-id values from literals and text bindings; computed
 * expressions are skipped. The call-id column name is host-configurable
 * (the manifest's `columns.callId`, docs/naming.md), so callers pass it
 * in — nothing here hardcodes `call_id`.
 */

import type { BindingValue } from "@sqlite-host/runtime-types";

export type SqlTokenKind =
  | "identifier"
  | "quoted-identifier"
  | "string"
  | "number"
  | "parameter"
  | "punct";

export type ParameterPrefix = ":" | "@" | "$";

export interface SqlToken {
  kind: SqlTokenKind;
  /**
   * For strings/quoted identifiers: the unescaped inner text. For
   * parameters: the bare name (prefix stripped). Otherwise the raw text.
   */
  value: string;
  /** For parameters only: the prefix character this occurrence used. */
  prefix?: ParameterPrefix;
}

function isIdentStart(ch: string): boolean {
  return (ch >= "a" && ch <= "z") || (ch >= "A" && ch <= "Z") || ch === "_";
}

function isIdentPart(ch: string): boolean {
  // '$' is an identifier character in SQLite (docs/errors.md): a '$'
  // immediately preceded by an identifier character continues that
  // identifier instead of starting a parameter.
  return isParamPart(ch) || ch === "$";
}

function isParamPart(ch: string): boolean {
  return isIdentStart(ch) || (ch >= "0" && ch <= "9");
}

function isDigit(ch: string): boolean {
  return ch >= "0" && ch <= "9";
}

/** Tokenize SQL, skipping whitespace and comments. */
export function tokenizeSql(sql: string): SqlToken[] {
  const tokens: SqlToken[] = [];
  let i = 0;
  const n = sql.length;
  while (i < n) {
    const ch = sql[i];
    if (ch === " " || ch === "\t" || ch === "\r" || ch === "\n" || ch === "\f") {
      i++;
      continue;
    }
    if (ch === "-" && sql[i + 1] === "-") {
      while (i < n && sql[i] !== "\n") i++;
      continue;
    }
    if (ch === "/" && sql[i + 1] === "*") {
      const end = sql.indexOf("*/", i + 2);
      i = end === -1 ? n : end + 2;
      continue;
    }
    if (ch === "'" || ch === '"') {
      const quote = ch;
      let value = "";
      i++;
      while (i < n) {
        if (sql[i] === quote) {
          if (sql[i + 1] === quote) {
            value += quote;
            i += 2;
            continue;
          }
          i++;
          break;
        }
        value += sql[i];
        i++;
      }
      tokens.push({
        kind: quote === "'" ? "string" : "quoted-identifier",
        value,
      });
      continue;
    }
    if (ch === ":" || ch === "@" || ch === "$") {
      let j = i + 1;
      while (j < n && isParamPart(sql[j])) j++;
      if (j > i + 1) {
        tokens.push({
          kind: "parameter",
          value: sql.slice(i + 1, j),
          prefix: ch as ParameterPrefix,
        });
        i = j;
        continue;
      }
      tokens.push({ kind: "punct", value: ch });
      i++;
      continue;
    }
    if (isIdentStart(ch)) {
      let j = i + 1;
      while (j < n && isIdentPart(sql[j])) j++;
      tokens.push({ kind: "identifier", value: sql.slice(i, j) });
      i = j;
      continue;
    }
    if (isDigit(ch)) {
      let j = i + 1;
      while (j < n && (isParamPart(sql[j]) || sql[j] === ".")) j++;
      tokens.push({ kind: "number", value: sql.slice(i, j) });
      i = j;
      continue;
    }
    tokens.push({ kind: "punct", value: ch });
    i++;
  }
  return tokens;
}

/** Named parameters referenced by the SQL — bare names, unique, in order. */
export function scanNamedParameters(sql: string): string[] {
  const names: string[] = [];
  for (const token of tokenizeSql(sql)) {
    if (token.kind === "parameter" && !names.includes(token.value)) {
      names.push(token.value);
    }
  }
  return names;
}

/** One row emitted by an INSERT (a VALUES group or the SELECT list). */
export interface InsertRowInfo {
  /** Statically-resolved call-id, or null when unresolvable. */
  callId: string | null;
}

export interface InsertInfo {
  /** Target table name, lowercased. */
  table: string;
  /** Explicit column list (lowercased), or null when implicit. */
  columns: string[] | null;
  rows: InsertRowInfo[];
}

function keywordAt(tokens: SqlToken[], index: number, word: string): boolean {
  const token = tokens[index];
  return token !== undefined && token.kind === "identifier" && token.value.toLowerCase() === word;
}

/** Split a token range into top-level (paren depth 0) comma groups. */
function splitTopLevel(tokens: SqlToken[], start: number, end: number): SqlToken[][] {
  const groups: SqlToken[][] = [];
  let current: SqlToken[] = [];
  let depth = 0;
  for (let i = start; i < end; i++) {
    const token = tokens[i];
    if (token.kind === "punct" && token.value === "(") depth++;
    if (token.kind === "punct" && token.value === ")") depth--;
    if (depth === 0 && token.kind === "punct" && token.value === ",") {
      groups.push(current);
      current = [];
      continue;
    }
    current.push(token);
  }
  if (current.length > 0) groups.push(current);
  return groups;
}

function resolveExpression(
  expr: SqlToken[] | undefined,
  bindings: Record<string, BindingValue>,
): string | null {
  if (expr === undefined || expr.length !== 1) return null;
  const token = expr[0];
  if (token.kind === "string") return token.value;
  if (token.kind === "parameter") {
    const binding = bindings[token.value];
    if (binding !== undefined && binding.type === "text") return binding.value;
  }
  return null;
}

function isPunctAt(token: SqlToken | undefined, value: string): boolean {
  return token !== undefined && token.kind === "punct" && token.value === value;
}

function isAtom(token: SqlToken | undefined): token is SqlToken {
  return token !== undefined && (token.kind === "string" || token.kind === "parameter");
}

/**
 * Statically-resolved `<callIdColumn> = <atom>` (and
 * `<atom> = <callIdColumn>`) comparison values, where the atom is a
 * single string literal or a parameter with a text binding.
 * Concatenations and other computed expressions are not atoms — they
 * are skipped by static call-id resolution (mirrors the Java
 * validator's SqlAnalyzer). `callIdColumn` is the manifest's
 * `columns.callId` (e.g. `call_id`).
 */
export function callIdFilters(
  tokens: SqlToken[],
  bindings: Record<string, BindingValue>,
  callIdColumn: string,
): string[] {
  const column = callIdColumn.toLowerCase();
  const filters: string[] = [];
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    if (
      (token.kind !== "identifier" && token.kind !== "quoted-identifier") ||
      token.value.toLowerCase() !== column
    ) {
      continue;
    }
    // forward form: <callIdColumn> = <atom> (not continued by || or .)
    if (
      isPunctAt(tokens[i + 1], "=") &&
      isAtom(tokens[i + 2]) &&
      !isPunctAt(tokens[i + 3], "|") &&
      !isPunctAt(tokens[i + 3], ".")
    ) {
      const value = resolveExpression([tokens[i + 2]], bindings);
      if (value !== null) filters.push(value);
    }
    // reverse form: <atom> = <callIdColumn> (atom not the tail of a concatenation)
    if (isPunctAt(tokens[i - 1], "=") && isAtom(tokens[i - 2]) && !isPunctAt(tokens[i - 3], "|")) {
      const value = resolveExpression([tokens[i - 2]], bindings);
      if (value !== null) filters.push(value);
    }
  }
  return filters;
}

const SELECT_TERMINATORS = new Set([
  "from",
  "where",
  "group",
  "having",
  "order",
  "limit",
  "union",
  "except",
  "intersect",
]);

/**
 * Analyze an INSERT statement: target table, explicit column list, and
 * the statically-resolvable call-id of each emitted row (`callIdColumn`
 * is the manifest's `columns.callId`). Returns null for non-INSERT
 * statements or unrecognized shapes.
 */
export function analyzeInsert(
  tokens: SqlToken[],
  bindings: Record<string, BindingValue>,
  callIdColumn: string,
): InsertInfo | null {
  // The INSERT keyword may be preceded by a WITH … CTE prefix: find the
  // first `insert` identifier, like the Java analyzer's indexOfIdent.
  let insertIndex = -1;
  for (let k = 0; k < tokens.length; k++) {
    if (keywordAt(tokens, k, "insert")) {
      insertIndex = k;
      break;
    }
  }
  if (insertIndex < 0) return null;
  let i = insertIndex + 1;
  if (keywordAt(tokens, i, "or")) i += 2; // INSERT OR REPLACE/IGNORE/...
  if (!keywordAt(tokens, i, "into")) return null;
  i++;
  let nameToken = tokens[i];
  if (
    nameToken === undefined ||
    (nameToken.kind !== "identifier" && nameToken.kind !== "quoted-identifier")
  ) {
    return null;
  }
  i++;
  // schema-qualified name: keep the rightmost part
  while (tokens[i]?.kind === "punct" && tokens[i]?.value === ".") {
    nameToken = tokens[i + 1];
    if (nameToken === undefined) return null;
    i += 2;
  }
  const table = nameToken.value.toLowerCase();

  let columns: string[] | null = null;
  if (tokens[i]?.kind === "punct" && tokens[i]?.value === "(") {
    columns = [];
    i++;
    while (i < tokens.length && !(tokens[i].kind === "punct" && tokens[i].value === ")")) {
      const token = tokens[i];
      if (token.kind === "identifier" || token.kind === "quoted-identifier") {
        columns.push(token.value.toLowerCase());
      }
      i++;
    }
    i++; // past ")"
  }

  // call-id position: explicit list, else first column per canonical DDL.
  const callIdIndex = columns === null ? 0 : columns.indexOf(callIdColumn.toLowerCase());

  const rows: InsertRowInfo[] = [];
  if (keywordAt(tokens, i, "values")) {
    i++;
    while (tokens[i]?.kind === "punct" && tokens[i]?.value === "(") {
      let depth = 1;
      const start = i + 1;
      let j = start;
      while (j < tokens.length && depth > 0) {
        if (tokens[j].kind === "punct" && tokens[j].value === "(") depth++;
        if (tokens[j].kind === "punct" && tokens[j].value === ")") depth--;
        if (depth > 0) j++;
      }
      const exprs = splitTopLevel(tokens, start, j);
      rows.push({
        callId:
          callIdIndex >= 0 ? resolveExpression(exprs[callIdIndex], bindings) : null,
      });
      i = j + 1;
      if (tokens[i]?.kind === "punct" && tokens[i]?.value === ",") i++;
      else break;
    }
  } else if (keywordAt(tokens, i, "select")) {
    i++;
    const start = i;
    let depth = 0;
    let j = start;
    while (j < tokens.length) {
      const token = tokens[j];
      if (token.kind === "punct" && token.value === "(") depth++;
      if (token.kind === "punct" && token.value === ")") depth--;
      if (
        depth === 0 &&
        token.kind === "identifier" &&
        SELECT_TERMINATORS.has(token.value.toLowerCase())
      ) {
        break;
      }
      j++;
    }
    const exprs = splitTopLevel(tokens, start, j);
    rows.push({
      callId: callIdIndex >= 0 ? resolveExpression(exprs[callIdIndex], bindings) : null,
    });
  } else {
    // INSERT INTO t DEFAULT VALUES, or unrecognized: no rows to resolve.
  }

  return { table, columns, rows };
}
