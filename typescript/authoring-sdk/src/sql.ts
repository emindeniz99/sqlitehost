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
  return isIdentStart(ch) || isDigit(ch) || ch === "$";
}

function isParamPart(ch: string): boolean {
  // SQLite's IdChar, which a parameter name runs over: letters, digits,
  // '_', '$', and any character above 0x7f. Cutting the name at its ASCII
  // head would report missing-binding for a name the author never wrote,
  // and the adapter conformance suite requires non-ASCII names to bind.
  //
  // Two further characters can appear *inside* a name and are handled by the
  // scan loop rather than here, because they are only legal in position: a
  // doubled colon (`:a::b`) and one trailing parenthesised group (`$a(1)`).
  // Both come from SQLite's TCL variable syntax, are compiled in by default,
  // and apply to every prefix — verified against the sqlite3 CLI 3.51.0,
  // where `:a::b` and `$a(1)` each bind as a *single* parameter whose name
  // carries the suffix. Splitting them, as this scanner used to, accepts the
  // payload that fails on every device and rejects the only one that runs.
  return isIdentPart(ch) || ch > "\u007f";
}

function isDigit(ch: string): boolean {
  return ch >= "0" && ch <= "9";
}

/**
 * The characters that separate tokens: C's `isspace()` set — space, `\t`,
 * `\n`, `\v`, `\f`, `\r`. The Java tokenizer enumerates the identical set,
 * and that agreement is the point: whitespace decides where one token ends,
 * so a byte one validator skips and the other does not makes the whole
 * statement analysis (the denylist included) diverge between them.
 *
 * It is deliberately two characters wider than SQLite's own
 * `sqlite3Isspace`:
 *
 * - **U+000B** (vertical tab), which SQLite omits — verified against the
 *   sqlite3 CLI 3.51.0, where an INSERT split by a vertical tab is a parse
 *   error while the form-feed version runs.
 * - **U+FEFF** (the UTF-8 BOM), which SQLite's *tokenizer* does accept as a
 *   separator wherever a token may start (tokenize.c gives 0xEF its own
 *   `CC_BOM` class and returns `TK_SPACE`), and which it treats as an
 *   identifier character only when it continues one. Measured on the CLI
 *   3.51.0 and Python's 3.53.4: `<BOM>SELECT 1`, `SELECT <BOM>1` and
 *   `DELETE <BOM> FROM t` all run, while `DELETE<BOM>FROM t` is a syntax
 *   error because the BOM is welded onto `DELETE`. Not skipping it meant
 *   token 0 of `<BOM>PRAGMA writable_schema = ON` was punctuation, so
 *   `leadingKeyword` and `writeTarget` both returned null and one invisible
 *   character disabled the forbidden-statement and protocol-table-write
 *   denylists outright.
 *
 * Over-skipping is the fail-safe direction in both cases: the extra
 * character can only appear where SQLite refuses to prepare (a
 * BOM *inside* an identifier), so treating it as a separator costs no
 * runnable script a false positive, while not skipping it hides a denied
 * statement from the lint.
 */
function isSqlWhitespace(ch: string): boolean {
  return (
    ch === " " ||
    ch === "\t" ||
    ch === "\n" ||
    ch === "\v" ||
    ch === "\f" ||
    ch === "\r" ||
    ch === "\uFEFF"
  );
}

/** Tokenize SQL, skipping whitespace and comments. */
export function tokenizeSql(sql: string): SqlToken[] {
  const tokens: SqlToken[] = [];
  let i = 0;
  const n = sql.length;
  while (i < n) {
    const ch = sql[i];
    if (isSqlWhitespace(ch)) {
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
      // SQLite's variable grammar (see isParamPart for the two suffix forms).
      let j = i + 1;
      let idChars = 0;
      let illegal = false;
      while (j < n) {
        const c = sql[j];
        if (isParamPart(c)) {
          idChars++;
          j++;
        } else if (c === "(" && idChars > 0) {
          // A single trailing '(...)' group closes the name.
          j++;
          while (j < n && sql[j] !== ")" && !isSqlWhitespace(sql[j])) j++;
          if (j < n && sql[j] === ")") {
            j++;
          } else {
            illegal = true;
          }
          break;
        } else if (c === ":" && sql[j + 1] === ":") {
          j += 2;
        } else {
          break;
        }
      }
      if (illegal) {
        // SQLite lexes an unterminated '(' group as one illegal token;
        // consume the same span so the stray '(' cannot unbalance the paren
        // depth the statement analysis tracks.
        tokens.push({ kind: "punct", value: sql.slice(i, j) });
        i = j;
        continue;
      }
      if (idChars > 0) {
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

/**
 * Named parameters referenced by the SQL — bare names, unique, in order.
 *
 * The Set carries the membership test; the array carries the order,
 * which callers rely on for finding order. Deduplicating with
 * `names.includes` instead is quadratic in a payload-controlled count,
 * and this package has no input cap — a statement with 100k parameters
 * is a hang, which in a browser tab is the whole page.
 */
export function scanNamedParameters(sql: string): string[] {
  const names: string[] = [];
  const seen = new Set<string>();
  for (const token of tokenizeSql(sql)) {
    if (token.kind === "parameter" && !seen.has(token.value)) {
      seen.add(token.value);
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
 *
 * One pass, one stack of open parens. The obvious shape — find each
 * `identifier(`, then scan forward for its matching `)` — is quadratic in
 * the nesting depth, and SQL nests: `abs(abs(abs(…)))` a hundred thousand
 * deep is 10^10 token visits, minutes of CPU inside a lint an author runs
 * on every save. The single scan below is proportional to the token count
 * whatever the shape.
 *
 * A *quoted* name in call position counts too, in every quoting form:
 * `"random"()`, `[random]()` and `` `random`() `` all invoke random()
 * in SQLite. Matching bare identifiers alone let an author bypass every
 * lint that reads this list simply by quoting the name.
 */
export function functionCalls(tokens: SqlToken[]): SqlFunctionCall[] {
  const calls: SqlFunctionCall[] = [];
  const stack: ParenFrame[] = [];
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    const top = stack.length === 0 ? undefined : stack[stack.length - 1];
    if (isPunctAt(token, "(")) {
      if (top !== undefined) noteNestedToken(top);
      // A call frame remembers WHERE its result goes, so the finished
      // list stays in opening-token order however the nesting closes.
      let slot = -1;
      if (i > 0 && isIdentToken(tokens[i - 1])) {
        slot = calls.length;
        calls.push({ name: tokens[i - 1].value, argCount: UNKNOWN_ARGS, hasNowArg: false });
      }
      stack.push({ slot, commas: 0, sawArgToken: false, argTokens: 0, argIsNow: false, nowSeen: false });
      continue;
    }
    if (isPunctAt(token, ")")) {
      const frame = stack.pop();
      if (frame === undefined) continue; // stray ')' — closes nothing
      if (frame.slot >= 0) {
        const call = calls[frame.slot];
        call.argCount = frame.sawArgToken ? frame.commas + 1 : 0;
        call.hasNowArg = frame.nowSeen || (frame.argTokens === 1 && frame.argIsNow);
      }
      if (stack.length > 0) noteNestedToken(stack[stack.length - 1]);
      continue;
    }
    if (top === undefined) continue; // outside every paren — nothing to count
    if (isPunctAt(token, ",")) {
      top.commas++;
      top.sawArgToken = true;
      if (top.argTokens === 1 && top.argIsNow) top.nowSeen = true;
      top.argTokens = 0;
      top.argIsNow = false;
      continue;
    }
    if (top.argTokens === 0) {
      top.argIsNow = token.kind === "string" && token.value.toLowerCase() === "now";
    }
    top.argTokens++;
    top.sawArgToken = true;
  }
  // Frames still open at end of input have no matching ')': arity is
  // unknowable, but a 'now' already closed off by a top-level comma was
  // seen for certain.
  for (const frame of stack) {
    if (frame.slot >= 0) calls[frame.slot].hasNowArg = frame.nowSeen;
  }
  return calls;
}

/** One open `(` — a call when `slot` points at its entry in the result. */
interface ParenFrame {
  slot: number;
  commas: number;
  sawArgToken: boolean;
  /** Tokens in the current top-level argument, CLAMPED at 2 (see below). */
  argTokens: number;
  argIsNow: boolean;
  /** A completed top-level argument was exactly the literal `'now'`. */
  nowSeen: boolean;
}

/**
 * Account for a whole nested paren group in the enclosing frame with O(1)
 * work — the step that makes the pass linear.
 *
 * Walking every enclosing frame per token would keep the cost quadratic in
 * the nesting depth, which is the bug this replaced. It is unnecessary
 * because an enclosing frame only ever asks two questions: whether its
 * current argument holds any token at all, and whether that argument is
 * EXACTLY one token which is `'now'`. An argument containing a nested group
 * already fails the second test — the group's own `(` and `)` are two
 * tokens — so clamping the count at 2 is not an approximation: no reachable
 * read can tell the difference. Commas inside the group belong to the
 * group, never to the enclosing frame, so they need no propagation at all.
 */
function noteNestedToken(frame: ParenFrame): void {
  frame.sawArgToken = true;
  frame.argTokens = 2;
}

/**
 * Every identifier token NOT immediately followed by `(` — a name used
 * bare, which for a table-valued function is the argument-less spelling.
 *
 * `functionCalls` only ever saw `identifier(`, so a TVF written without an
 * argument list was invisible to every rule that reads the call list:
 * `SELECT * FROM pragma_optimize` runs ANALYZE, and
 * `SELECT * FROM pragma_table_list` needs 3.37, and neither was seen. Both
 * spellings are legal SQLite (`pragma_optimize(0xfffe)` too), so both have
 * to be scanned. Callers filter by name — this returns every bare
 * identifier, including ordinary tables and columns.
 *
 * Mirrors the Java SqlAnalyzer.bareIdentifiers.
 */
export function bareIdentifiers(tokens: SqlToken[]): string[] {
  const names: string[] = [];
  for (let i = 0; i < tokens.length; i++) {
    if (isIdentToken(tokens[i]) && !isPunctAt(tokens[i + 1], "(")) {
      names.push(tokens[i].value);
    }
  }
  return names;
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

/**
 * Every token kind SQLite accepts where a **name** is required (a table,
 * a column): the two identifier kinds plus a single-quoted string.
 *
 * The string case is SQLite's documented MySQL-compatibility misfeature —
 * "If a keyword in single quotes is used in a context where an identifier is
 * allowed but where a string literal is not allowed, then the token is
 * understood to be an identifier" (sqlite.org/lang_keywords.html). Verified
 * against the sqlite3 CLI 3.51.0: `DELETE FROM 'pending_host_calls'`,
 * `UPDATE 'result_get_value' SET …`, `INSERT INTO 'result_get_value' (…)`
 * and `DELETE FROM main.'pending_host_calls'` all compile and run. Treating
 * `'…'` as a value-only token in name position left every one of them
 * invisible to protocol-table-write, the INSERT analysis and result-read
 * lineage — a one-character bypass of the whole denylist.
 *
 * This is deliberately NOT used in value position: `resolveExpression` and
 * the call-id atoms must keep reading `'x'` as the literal it is there, or
 * static call-id resolution changes meaning. Mirrors the Java
 * SqlAnalyzer.isName.
 */
function isNameToken(token: SqlToken | undefined): token is SqlToken {
  return (
    token !== undefined &&
    (token.kind === "identifier" ||
      token.kind === "quoted-identifier" ||
      token.kind === "string")
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
 * field: adapters disagree about the tail (the native adapter rejects a
 * trailing statement without stepping anything, ADO.NET adapters execute
 * it). Without this check a leading no-op —
 * `SELECT 1; PRAGMA writable_schema = ON` — anchors leadingKeyword/writeTarget
 * on the harmless `SELECT`, bypassing the forbidden-statement and
 * protocol-table-write denylists entirely.
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

/**
 * Read `[schema.]table` at `start`, keeping the last component (lowercased).
 * Both components accept every name token, single-quoted included — SQLite
 * resolves `main.'pending_host_calls'` as a name, and stopping at `main`
 * reported the harmless schema as the write target.
 */
function qualifiedName(tokens: SqlToken[], start: number): string | null {
  let pos = start;
  if (!isNameToken(tokens[pos])) return null;
  let name = tokens[pos].value;
  pos++;
  while (isPunctAt(tokens[pos], ".") && isNameToken(tokens[pos + 1])) {
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

/**
 * A BARE keyword: an identifier token written without any quoting form.
 *
 * Every syntax detector below tests keywords through this and never through
 * `identEquals`, and that distinction is the whole false-positive story. A
 * SQLite keyword cannot be written delimited — `"full"` is a column or table
 * name (or, under the double-quote fallback, a string), never the FULL of
 * `FULL JOIN` — so `SELECT * FROM t "full" JOIN u ON 1` is ordinary 3.19.3
 * SQL that a text-level match would reject. Since this lint is an ERROR that
 * blocks publication, a false positive costs as much as a miss. The
 * determinism lint draws the same line for `CURRENT_DATE`.
 */
function bareKeyword(token: SqlToken | undefined, word: string): boolean {
  return token !== undefined && token.kind === "identifier" && token.value.toLowerCase() === word;
}

/** Statement verbs that may carry a RETURNING clause. */
const RETURNING_VERBS = new Set(["insert", "replace", "update", "delete"]);

/**
 * The statement's verb after an optional `WITH …` CTE prefix, lowercased, or
 * null when the statement does not start with an identifier. The anchor the
 * statement-scoped detectors (upsert, returning, update-from) need, and the
 * same anchoring `writeTarget` uses — scanning for the verb anywhere in the
 * token stream would read `SELECT "update" FROM t` as an UPDATE.
 */
function anchoredVerb(tokens: SqlToken[]): string | null {
  const pos = skipCtePrefix(tokens);
  const token = tokens[pos];
  return isIdentToken(token) ? token.value.toLowerCase() : null;
}

/**
 * Whether the statement writes `INSERT INTO <table> AS <alias>` — SQLite
 * 3.24.0 syntax, which arrived with UPSERT so the DO UPDATE clause could name
 * the target (`SYNTAX_MIN_VERSION insert-alias`).
 *
 * Anchored exactly like `writeTarget`: verb, optional `OR …` conflict clause,
 * `INTO`, the qualified name, and only then the `AS`. Matching `AS` anywhere
 * after an INSERT would catch every `INSERT INTO t SELECT x AS y FROM u`.
 */
function hasInsertAlias(tokens: SqlToken[]): boolean {
  let pos = skipCtePrefix(tokens);
  if (!identEquals(tokens[pos], "insert") && !identEquals(tokens[pos], "replace")) return false;
  pos++;
  for (let guard = 0; guard < 2 && !identEquals(tokens[pos], "into"); guard++) {
    if (!isIdentToken(tokens[pos])) return false;
    pos++;
  }
  if (!identEquals(tokens[pos], "into")) return false;
  pos++;
  if (!isNameToken(tokens[pos])) return false;
  pos++;
  while (isPunctAt(tokens[pos], ".") && isNameToken(tokens[pos + 1])) pos += 2;
  return bareKeyword(tokens[pos], "as");
}

/**
 * Whether a bare `returning` at `index` is the RETURNING clause rather than a
 * column that happens to be spelled that way. SQLite keeps RETURNING usable
 * as an identifier, so position is all there is to go on:
 *
 * - it is preceded by the END of the preceding clause — a `)`, or any
 *   non-punctuation token (a name, literal or parameter). Anything else
 *   (`SET x = returning`, `(returning`, `a || returning`) is an expression
 *   operand;
 * - it is followed by the START of a result list — `*`, `(`, or a
 *   non-punctuation token. `WHERE returning > 0` and `SET returning = 1` are
 *   both excluded by that.
 *
 * The residual, and it is documented in docs/validation.md rather than
 * papered over: a column literally named `returning`, used bare at depth 0 of
 * a write statement in a spot that passes both tests — `WHERE returning IS
 * NULL` — is still reported. Spelling it `"returning"` is the escape, the
 * same one the determinism lint gives `"current_date"`.
 */
function isReturningClause(tokens: SqlToken[], index: number): boolean {
  const before = tokens[index - 1];
  if (before !== undefined && before.kind === "punct" && before.value !== ")") return false;
  const after = tokens[index + 1];
  if (after !== undefined && after.kind === "punct" && after.value !== "*" && after.value !== "(") {
    return false;
  }
  return true;
}

/**
 * The `SYNTAX_MIN_VERSION` feature ids the statement uses, first occurrence
 * first, deduplicated — the input to the sqlite-version-too-low-for-syntax
 * lint (docs/validation.md).
 *
 * Token patterns, not a grammar. The version half of the SQLite contract
 * cannot be enforced by preparing the statement (docs/validation.md §3: the
 * validator's engine is always far newer than the floor), so the only thing
 * left is to recognize the constructs lexically. Each detector is written to
 * be wrong in the SILENT direction wherever the token stream is ambiguous,
 * because the finding is an error.
 *
 * Deliberately not detected, for one reason each: `GENERATED ALWAYS AS`
 * (3.31.0), `STRICT` / `WITHOUT ROWID` (3.37.0) and `VACUUM INTO` (3.27.0)
 * only occur in statements `forbidden-statement` already refuses, so a
 * detector could only ever pile a second finding onto a rejected statement;
 * `TRUE` / `FALSE` (3.23.0) tokenize identically to a column reference, which
 * is exactly what a pre-3.23 engine parses them as.
 *
 * Mirrors the Java SqlAnalyzer.syntaxFeatures token for token.
 */
export function syntaxFeatures(tokens: SqlToken[]): string[] {
  const features: string[] = [];
  const seen = new Set<string>();
  const add = (feature: string): void => {
    if (seen.has(feature)) return;
    seen.add(feature);
    features.push(feature);
  };

  if (hasInsertAlias(tokens)) add("insert-alias");

  const verb = anchoredVerb(tokens);
  const isInsert = verb === "insert" || verb === "replace";
  const canReturn = verb !== null && RETURNING_VERBS.has(verb);
  let depth = 0;
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    // `->` and `->>`. The two tokenizers split them differently — the Java
    // one folds `>>` into a single operator token, this one emits every
    // unrecognized character on its own — so the pattern both agree on is
    // `-` followed by a punct token that STARTS with `>`. Whitespace is gone
    // by now, so `a - > b` matches too; that is a syntax error on every
    // engine, which makes over-matching there the harmless direction.
    if (
      isPunctAt(token, "-") &&
      tokens[i + 1] !== undefined &&
      tokens[i + 1].kind === "punct" &&
      tokens[i + 1].value.startsWith(">")
    ) {
      add("json-arrow-operators");
    }
    if (isPunctAt(token, "(")) {
      depth++;
      continue;
    }
    if (isPunctAt(token, ")")) {
      depth--;
      continue;
    }
    if (bareKeyword(token, "over") && isPunctAt(tokens[i + 1], "(")) {
      add("window-functions");
    }
    if (
      bareKeyword(token, "filter") &&
      isPunctAt(tokens[i + 1], "(") &&
      bareKeyword(tokens[i + 2], "where")
    ) {
      add("aggregate-filter");
    }
    if (
      bareKeyword(token, "nulls") &&
      (bareKeyword(tokens[i + 1], "first") || bareKeyword(tokens[i + 1], "last"))
    ) {
      add("nulls-first-last");
    }
    // `AS [NOT] MATERIALIZED (`. The trailing `(` is what separates the CTE
    // hint from a result column aliased `materialized`.
    if (bareKeyword(token, "as")) {
      const skipNot = bareKeyword(tokens[i + 1], "not") ? 1 : 0;
      if (
        bareKeyword(tokens[i + 1 + skipNot], "materialized") &&
        isPunctAt(tokens[i + 2 + skipNot], "(")
      ) {
        add("materialized-cte");
      }
    }
    if (
      (bareKeyword(token, "right") || bareKeyword(token, "full")) &&
      (bareKeyword(tokens[i + 1], "join") || bareKeyword(tokens[i + 1], "outer"))
    ) {
      add("right-full-join");
    }
    if (bareKeyword(token, "is")) {
      const skipNot = bareKeyword(tokens[i + 1], "not") ? 1 : 0;
      if (
        bareKeyword(tokens[i + 1 + skipNot], "distinct") &&
        bareKeyword(tokens[i + 2 + skipNot], "from")
      ) {
        add("is-distinct-from");
      }
    }
    // UPSERT is an INSERT clause. The other `ON CONFLICT` in SQLite is a
    // column/table CONSTRAINT clause, which is pre-floor syntax and lives in
    // a CREATE TABLE the statement denylist already refuses.
    if (isInsert && bareKeyword(token, "on") && bareKeyword(tokens[i + 1], "conflict")) {
      add("upsert");
    }
    if (canReturn && depth === 0 && bareKeyword(token, "returning") && isReturningClause(tokens, i)) {
      add("returning");
    }
    // A top-level FROM in an UPDATE. Every pre-3.33 FROM inside an UPDATE
    // belongs to a subquery, and a subquery is parenthesized.
    if (verb === "update" && depth === 0 && bareKeyword(token, "from")) {
      add("update-from");
    }
  }
  return features;
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
  if (!isNameToken(nameToken)) {
    return null;
  }
  i++;
  // schema-qualified name: keep the rightmost part
  while (tokens[i]?.kind === "punct" && tokens[i]?.value === ".") {
    const next = tokens[i + 1];
    if (!isNameToken(next)) return null;
    nameToken = next;
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
      if (isNameToken(token)) {
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
