/**
 * Lexical SQL scanning for the static authoring lint. The named
 * parameter scanner implements the shared algorithm from
 * docs/errors.md: scan for `:name` / `@name` / `$name` while skipping
 * string literals ('…' with '' escapes) and quoted identifiers —
 * double-quoted ("…" with "" escapes), bracket ([…], ends at the first
 * ']', no escape) and backtick (`…` with `` `` `` escapes) — plus line
 * comments (--) and block comments. The same algorithm is used by the
 * C# runtime and the Java validator.
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
    if (ch === "`") {
      // Backtick-quoted identifier (MySQL compat) with doubled-backtick
      // escapes, mirroring the "…" loop above. Emits the same
      // quoted-identifier kind so INSERT/lineage analysis accepts it.
      let value = "";
      i++;
      while (i < n) {
        if (sql[i] === "`") {
          if (sql[i + 1] === "`") {
            value += "`";
            i += 2;
            continue;
          }
          i++;
          break;
        }
        value += sql[i];
        i++;
      }
      tokens.push({ kind: "quoted-identifier", value });
      continue;
    }
    if (ch === "[") {
      // Bracket-quoted identifier (MS Access/SQL Server compat): no
      // escape mechanism — the identifier ends at the first ']'.
      let value = "";
      i++;
      while (i < n && sql[i] !== "]") {
        value += sql[i];
        i++;
      }
      if (i < n) i++; // past ']'
      tokens.push({ kind: "quoted-identifier", value });
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

/** A single-parameter cell of an INSERT row bound to a known column. */
export interface InsertCell {
  /** Target column name, lowercased. */
  column: string;
  /** Bare parameter name feeding the column (single-parameter cells only). */
  param: string;
}

/** One row emitted by an INSERT (a VALUES group or the SELECT list). */
export interface InsertRowInfo {
  /** Statically-resolved call-id, or null when unresolvable. */
  callId: string | null;
  /**
   * Cells whose value is a single parameter and whose column is known
   * (explicit column list only) — for binding-type checks. Empty when the
   * column list is implicit or no cell is a bare parameter.
   */
  cells: InsertCell[];
}

export interface InsertInfo {
  /** Target table name, lowercased. */
  table: string;
  /** Explicit column list (lowercased), or null when implicit. */
  columns: string[] | null;
  rows: InsertRowInfo[];
}

/** Single-parameter cells of one row, paired with their (explicit) columns. */
function paramCells(columns: string[] | null, exprs: SqlToken[][]): InsertCell[] {
  if (columns === null) return [];
  const cells: InsertCell[] = [];
  const n = Math.min(columns.length, exprs.length);
  for (let i = 0; i < n; i++) {
    const expr = exprs[i];
    if (expr.length === 1 && expr[0].kind === "parameter") {
      cells.push({ column: columns[i], param: expr[0].value });
    }
  }
  return cells;
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

/**
 * One `identifier(...)` call extracted from the token stream.
 * `argCount` is the number of top-level arguments, or `UNKNOWN_ARGS`
 * when the matching `)` is missing (malformed SQL — prepare-only
 * validation reports it).
 */
export interface SqlFunctionCall {
  /** Function name as written (SQL function names are case-insensitive). */
  name: string;
  argCount: number;
  /**
   * Whether some top-level argument is the string literal `'now'`
   * (case-insensitive) — what makes a date/time built-in read the wall
   * clock (the determinism lint, docs/validation.md).
   */
  hasNowArg: boolean;
}

/** The closing `)` was never found — arity is unknowable. */
export const UNKNOWN_ARGS = -1;

/**
 * Extract every `identifier(...)` function call: an identifier token
 * immediately followed by `(`, with the argument count taken by a
 * top-level comma scan to the matching `)`. String literals and
 * comments never confuse the scan — the tokenizer already collapsed
 * them. Calls nested in another call's arguments are extracted as
 * their own entries (mirrors the Java validator's SqlAnalyzer).
 */
export function functionCalls(tokens: SqlToken[]): SqlFunctionCall[] {
  const calls: SqlFunctionCall[] = [];
  for (let i = 0; i + 1 < tokens.length; i++) {
    if (tokens[i].kind === "identifier" && isPunctAt(tokens[i + 1], "(")) {
      calls.push({
        name: tokens[i].value,
        argCount: countArgs(tokens, i + 2),
        hasNowArg: hasNowArg(tokens, i + 2),
      });
    }
  }
  return calls;
}

/** Count top-level arguments from just after `(` to the matching `)`. */
function countArgs(tokens: SqlToken[], start: number): number {
  let depth = 1;
  let commas = 0;
  let sawArgToken = false;
  for (let pos = start; pos < tokens.length; pos++) {
    const token = tokens[pos];
    if (isPunctAt(token, "(")) {
      depth++;
    } else if (isPunctAt(token, ")")) {
      depth--;
      if (depth === 0) {
        return sawArgToken ? commas + 1 : 0;
      }
    } else if (isPunctAt(token, ",") && depth === 1) {
      commas++;
    }
    sawArgToken = true;
  }
  return UNKNOWN_ARGS;
}

/**
 * Whether some top-level argument from just after `(` to the matching
 * `)` is exactly the string literal `'now'` (case-insensitive). Only a
 * bare literal counts: `datetime('now')` reads the clock, `datetime(:when)`
 * does not, and a literal nested inside a larger expression is not the
 * argument itself. Mirrors the Java validator's SqlAnalyzer.
 */
function hasNowArg(tokens: SqlToken[], start: number): boolean {
  let depth = 1;
  let argTokens = 0;
  let argIsNow = false;
  for (let pos = start; pos < tokens.length; pos++) {
    const token = tokens[pos];
    if (isPunctAt(token, ")")) {
      depth--;
      if (depth === 0) {
        return argTokens === 1 && argIsNow;
      }
    } else if (isPunctAt(token, "(")) {
      depth++;
    } else if (isPunctAt(token, ",") && depth === 1) {
      if (argTokens === 1 && argIsNow) return true;
      argTokens = 0;
      argIsNow = false;
      continue;
    }
    if (argTokens === 0) {
      argIsNow = token.kind === "string" && token.value.toLowerCase() === "now";
    }
    argTokens++;
  }
  return false;
}

/**
 * Both identifier token kinds. The Java tokenizer emits a single IDENT kind
 * for bare, double-quoted, bracket- and backtick-quoted names, so every
 * helper that must agree with the Java validator token-for-token treats them
 * alike here too — the fixture corpus already carries `[call_get_value]` and
 * `` `call_get_value` `` targets.
 */
function isIdentToken(token: SqlToken | undefined): token is SqlToken {
  return (
    token !== undefined &&
    (token.kind === "identifier" || token.kind === "quoted-identifier")
  );
}

function identEquals(token: SqlToken | undefined, word: string): boolean {
  return isIdentToken(token) && token.value.toLowerCase() === word;
}

/**
 * The statement's first meaningful token, lowercased, when that token is an
 * identifier — the anchor of the forbidden-statement lint
 * (docs/validation.md). `tokenizeSql` has already dropped whitespace and both
 * comment forms, so token 0 *is* the first meaningful token; nothing extra is
 * needed to be comment-aware. Returns null for an empty statement or one
 * starting with a non-identifier, so a leading `'PRAGMA'` string literal is
 * never mistaken for the PRAGMA statement. Mirrors the Java SqlAnalyzer.
 */
export function leadingKeyword(tokens: SqlToken[]): string | null {
  if (!isIdentToken(tokens[0])) return null;
  return tokens[0].value.toLowerCase();
}

/**
 * Whether the token stream holds more than one SQL statement: a top-level
 * (paren depth 0) `;` punctuation token followed by at least one further
 * token — the anchor of the multiple-statements lint (docs/validation.md).
 * A trailing `;` that merely terminates a single statement (nothing follows
 * it) is legal and not flagged. Comments and string literals never trigger
 * it: the tokenizer already collapsed them, so a `;` inside `'…'` or a `--`
 * line comment is not a punctuation token here. Mirrors the Java SqlAnalyzer.
 *
 * This matters because the protocol contract is one statement per `sql`
 * field: the native adapter's prepare_v2 compiles only the FIRST statement
 * and silently drops the tail. Without this check a leading no-op —
 * `SELECT 1; PRAGMA writable_schema = ON` — anchors leadingKeyword/writeTarget
 * on the harmless `SELECT`, bypassing the forbidden-statement and
 * protocol-table-write denylists entirely, and silently discards the
 * author's real (rejected) statement.
 */
export function hasTrailingStatement(tokens: SqlToken[]): boolean {
  let depth = 0;
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    if (isPunctAt(token, "(")) {
      depth++;
    } else if (isPunctAt(token, ")")) {
      depth--;
    } else if (depth === 0 && isPunctAt(token, ";")) {
      return i + 1 < tokens.length;
    }
  }
  return false;
}

/** Index just past the `)` matching the `(` at `open`. */
function skipBalanced(tokens: SqlToken[], open: number): number {
  let depth = 0;
  for (let pos = open; pos < tokens.length; pos++) {
    if (isPunctAt(tokens[pos], "(")) depth++;
    else if (isPunctAt(tokens[pos], ")")) {
      depth--;
      if (depth === 0) return pos + 1;
    }
  }
  return tokens.length;
}

/**
 * Index of the statement verb after an optional `WITH [RECURSIVE]` CTE prefix
 * (`name [(cols)] AS [[NOT] MATERIALIZED] (body) [, …]`), or 0 when there is
 * no such prefix. Each parenthesized group is skipped by a balanced scan, so
 * a CTE body containing its own commas, subqueries, or the word `begin` never
 * confuses the walk. Mirrors the Java SqlAnalyzer.
 */
function skipCtePrefix(tokens: SqlToken[]): number {
  if (!identEquals(tokens[0], "with")) return 0;
  let pos = 1;
  if (identEquals(tokens[pos], "recursive")) pos++;
  while (pos < tokens.length) {
    if (!isIdentToken(tokens[pos])) return tokens.length; // unrecognized shape
    pos++; // CTE name
    if (isPunctAt(tokens[pos], "(")) pos = skipBalanced(tokens, pos); // column list
    if (identEquals(tokens[pos], "as")) pos++;
    if (identEquals(tokens[pos], "not")) pos++;
    if (identEquals(tokens[pos], "materialized")) pos++;
    if (!isPunctAt(tokens[pos], "(")) return tokens.length; // unrecognized shape
    pos = skipBalanced(tokens, pos); // CTE body
    if (isPunctAt(tokens[pos], ",")) {
      pos++;
      continue; // another CTE
    }
    return pos;
  }
  return pos;
}

/** Read `[schema.]table` at `start`, keeping the last component (lowercased). */
function qualifiedName(tokens: SqlToken[], start: number): string | null {
  let pos = start;
  if (!isIdentToken(tokens[pos])) return null;
  let name = tokens[pos].value;
  pos++;
  while (isPunctAt(tokens[pos], ".") && isIdentToken(tokens[pos + 1])) {
    name = tokens[pos + 1].value;
    pos += 2;
  }
  return name.toLowerCase();
}

/**
 * The single table an INSERT / UPDATE / DELETE writes (lowercased), or null
 * when the statement is not a write — the anchor of the protocol-table-write
 * lint (docs/validation.md).
 *
 * Unlike `analyzeInsert`, the verb is anchored at the start of the statement
 * (after an optional `WITH …` CTE prefix) instead of being matched anywhere
 * in the token stream. That matters because this lint raises an ERROR that
 * blocks publication: a scan-anywhere match would read
 * `SELECT "delete" FROM result_x` as a DELETE against `result_x` and reject
 * the single most important legal pattern — reading a result table. Skipping
 * the CTE prefix rather than only looking at token 0 is equally load-bearing
 * in the other direction: a bare `WITH d AS (SELECT 1) INSERT INTO result_x …`
 * would otherwise slip past the lint entirely. Mirrors the Java SqlAnalyzer.
 */
export function writeTarget(tokens: SqlToken[]): string | null {
  let pos = skipCtePrefix(tokens);
  if (!isIdentToken(tokens[pos])) return null;
  if (identEquals(tokens[pos], "insert") || identEquals(tokens[pos], "replace")) {
    pos++;
    // INSERT OR REPLACE / OR IGNORE / … — at most two idents before INTO.
    for (let guard = 0; guard < 2 && !identEquals(tokens[pos], "into"); guard++) {
      if (!isIdentToken(tokens[pos])) return null;
      pos++;
    }
    if (!identEquals(tokens[pos], "into")) return null;
    return qualifiedName(tokens, pos + 1);
  }
  if (identEquals(tokens[pos], "update")) {
    pos++;
    // UPDATE OR ROLLBACK / OR ABORT / … — one conflict-clause ident.
    if (identEquals(tokens[pos], "or") && tokens[pos + 1] !== undefined) pos += 2;
    return qualifiedName(tokens, pos);
  }
  if (identEquals(tokens[pos], "delete")) {
    pos++;
    if (!identEquals(tokens[pos], "from")) return null;
    return qualifiedName(tokens, pos + 1);
  }
  return null;
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

  // Optional `AS <alias>` between the table name and the column list
  // (valid SQLite >= 3.24.0, e.g. INSERT INTO t AS c (...) …). Skip it
  // so the explicit column list is still recognized. Only the
  // `AS <ident>` form is handled — a bare alias is a syntax error for
  // INSERT targets, and VALUES/SELECT/DEFAULT are identifiers that must
  // not be swallowed.
  if (keywordAt(tokens, i, "as")) {
    i++;
    if (tokens[i]?.kind === "identifier" || tokens[i]?.kind === "quoted-identifier") i++;
  }

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
        cells: paramCells(columns, exprs),
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
      cells: paramCells(columns, exprs),
    });
  } else {
    // INSERT INTO t DEFAULT VALUES, or unrecognized: no rows to resolve.
  }

  return { table, columns, rows };
}
