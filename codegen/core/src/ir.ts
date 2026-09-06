/**
 * Language-neutral intermediate representation (IR) for a SqliteHost
 * host library. The TypeSpec frontend produces this IR; every emitter
 * (manifest, C#, Java, TypeScript) consumes it. The canonical manifest
 * JSON (fixtures/manifests/*.manifest.json) is the serialized form of
 * this IR — see manifest.ts for the canonical serialization.
 */

export type ScalarTypeIr =
  | "int32"
  | "int64"
  | "boolean"
  | "string"
  | "bytes"
  | "float32"
  | "float64";

export interface NamingIr {
  callTablePrefix: string;
  resultTablePrefix: string;
  inputColumnPrefix: string;
  resultColumnPrefix: string;
  inputListTableInfix: string;
  resultListTableInfix: string;
  functionPrefix: string;
}

export interface ScalarFieldIr {
  /** TypeSpec/C#/Java/TS property name, camelCase (e.g. "defaultValue"). */
  propertyName: string;
  /** Logical SQL name, snake_case (e.g. "default_value"). */
  sqlName: string;
  /** Physical column name including prefix (e.g. "input_default_value"). */
  column: string;
  scalarType: ScalarTypeIr;
  optional: boolean;
}

export interface ListFieldIr {
  propertyName: string;
  sqlName: string;
  /** Physical child table name (e.g. "call_get_values__input_keys"). */
  childTable: string;
  itemModelName: string;
  itemFields: ScalarFieldIr[];
}

export interface ObjectShapeIr {
  modelName: string;
  fields: ScalarFieldIr[];
  listFields: ListFieldIr[];
}

/** Argument of an inline scalar function (input field, declaration order). */
export interface InlineArgIr {
  propertyName: string;
  sqlName: string;
  scalarType: ScalarTypeIr;
  optional: boolean;
}

/**
 * Inline scalar-function exposure of a method (feature inlineFunctions).
 * Present only when the method is eligible (mutates:false, scalar-only
 * input, exactly one scalar result field) and not opted out.
 */
export interface InlineIr {
  functionName: string;
  minArgs: number;
  maxArgs: number;
  args: InlineArgIr[];
  returns: {
    propertyName: string;
    sqlName: string;
    scalarType: ScalarTypeIr;
  };
}

export interface HostMethodIr {
  /** TypeSpec operation name (e.g. "GetValue"). */
  operationName: string;
  /** Logical method name used in the protocol (e.g. "getValue"). */
  methodName: string;
  /** Handler member name on the generated handler interface. */
  handlerName: string;
  apiLevel: number;
  /** True when the handler mutates host state (default). Non-mutating
   *  methods are eligible for inline function exposure. */
  mutates: boolean;
  callTable: string;
  resultTable: string;
  queueTrigger: string;
  input: ObjectShapeIr;
  result: ObjectShapeIr;
  /** Inline function exposure, or null when not exposed. */
  inline: InlineIr | null;
}

export interface QueueTableIr {
  name: string;
  columns: string[];
}

export interface InputsTableIr {
  name: string;
  columns: string[];
}

export interface VarsTableIr {
  name: string;
  columns: string[];
}

export interface ControlTableIr {
  name: string;
  columns: string[];
}

/**
 * Configurable column identifiers and the done-status literal — every
 * SQL-visible name a script author may want to rename. The halt/fail
 * action verbs, the engine string, and the trigger derivation rule
 * stay protocol constants (see docs/naming.md).
 */
export interface ColumnsIr {
  callId: string;
  itemIndex: string;
  status: string;
  doneValue: string;
  queueId: string;
  method: string;
  name: string;
  valueType: string;
  intValue: string;
  realValue: string;
  textValue: string;
  blobValue: string;
  action: string;
  message: string;
}

export interface ScriptEnvelopeIr {
  engine: string;
  bindingTypes: string[];
}

export interface HostLibraryIr {
  manifestVersion: 1;
  engine: string;
  library: {
    namespace: string;
    interfaceName: string;
    apiLevel: number;
    /** SQLITE_VERSION_NUMBER-style minimum (major*1000000 + minor*1000 + patch). */
    minSqliteVersionNumber: number;
    features: string[];
  };
  naming: NamingIr;
  columns: ColumnsIr;
  queueTable: QueueTableIr;
  inputsTable: InputsTableIr;
  varsTable: VarsTableIr;
  controlTable: ControlTableIr;
  scriptEnvelope: ScriptEnvelopeIr;
  methods: HostMethodIr[];
}

export const ENGINE_V1 = "sqlite-host-v1";

export const BINDING_TYPES_V1 = [
  "null",
  "int32",
  "int64",
  "bool",
  "text",
  "blob",
  "float32",
  "float64",
] as const;

export const FEATURES_V1 = [
  "typedNamedBindings",
  "splitResultTables",
  "scriptInputs",
  "scriptVars",
  "scriptControl",
] as const;

/**
 * Adapter-conditional feature: present in a manifest's library.features
 * only when the host exposes at least one inline function; supported by
 * a runtime only when its connection factory is function-capable.
 */
export const FEATURE_INLINE_FUNCTIONS = "inlineFunctions";

export const COLUMNS_V1: ColumnsIr = {
  callId: "call_id",
  itemIndex: "item_index",
  status: "status",
  doneValue: "done",
  queueId: "queue_id",
  method: "method",
  name: "name",
  valueType: "value_type",
  intValue: "int_value",
  realValue: "real_value",
  textValue: "text_value",
  blobValue: "blob_value",
  action: "action",
  message: "message",
};

/**
 * Reserved initial queue status. The queue-table DDL defaults new rows
 * to this literal and the runtime drain (DrainPendingCalls) selects rows
 * with status = PENDING_STATUS, marking a row done by resetting its
 * status to the configured doneValue. A doneValue equal to this sentinel
 * would leave drained rows selectable — re-drain / duplicate execution —
 * so it is rejected as a doneStatusValue (validate.ts). Single-sourced
 * here and referenced by the queue DDL template (ddl.ts).
 */
export const PENDING_STATUS = "pending";

/** Protocol verbs for the control table's action column (NOT configurable). */
export const CONTROL_ACTION_HALT = "halt";
export const CONTROL_ACTION_FAIL = "fail";

/**
 * SQLite built-in scalar/aggregate function names an inline function name
 * must not collide with (docs/naming.md). SQLite resolves function names
 * case-insensitively, so collision checks compare lowercased. Single-sourced
 * here and projected into each language's generated protocol constants
 * (docs/proposals/rule-parameters-as-data.md).
 */
export const SQLITE_BUILTIN_FUNCTIONS: readonly string[] = [
  "abs",
  "coalesce",
  "count",
  "sum",
  "min",
  "max",
  "length",
  "lower",
  "upper",
  "printf",
  "random",
  "replace",
  "round",
  "substr",
  "trim",
  "date",
  "time",
  "datetime",
  "ifnull",
  "nullif",
  "instr",
  "hex",
  "quote",
  "total",
  "group_concat",
  "typeof",
  "unicode",
  "char",
  "likelihood",
  "likely",
  "unlikely",
  "last_insert_rowid",
  "changes",
  "sqlite_version",
  "glob",
  "like",
  "zeroblob",
];

/**
 * SQLite built-ins that return a different value on every evaluation, so a
 * replay of the same script diverges from the original run (the determinism
 * lint, docs/validation.md). Every call is flagged regardless of arguments.
 * Names are compared lowercased (SQLite resolves them case-insensitively).
 * Single-sourced here and projected into each language's generated protocol
 * constants (docs/proposals/rule-parameters-as-data.md).
 */
export const NONDETERMINISTIC_FUNCTIONS_ALWAYS: readonly string[] = [
  "random",
  "randomblob",
];

/**
 * SQLite date/time built-ins that are nondeterministic only when they read
 * the wall clock — i.e. called with no arguments, or with the time value
 * `'now'`. `date(:day)` and `datetime('2020-01-01')` are reproducible and
 * must not be flagged. Same lowercased comparison and single-sourcing as
 * NONDETERMINISTIC_FUNCTIONS_ALWAYS.
 */
export const NONDETERMINISTIC_TIME_FUNCTIONS: readonly string[] = [
  "date",
  "time",
  "datetime",
  "julianday",
  "strftime",
];

/**
 * The three wall-clock KEYWORDS. SQLite's grammar spells these without an
 * argument list — `CURRENT_TIMESTAMP`, not `current_timestamp()`, which is a
 * syntax error — so a determinism lint that only inspects function calls
 * never sees them, even though `INSERT … VALUES (CURRENT_TIMESTAMP)` is
 * exactly as unreplayable as `datetime('now')`, which it does flag.
 *
 * Kept as a separate list rather than folded into
 * NONDETERMINISTIC_TIME_FUNCTIONS because the two are scanned differently:
 * that list is matched against parsed calls with their argument counts, this
 * one against bare identifier tokens. The names are compared lowercased —
 * SQLite's tokenizer is case-insensitive for keywords, so `current_date` and
 * `CURRENT_DATE` are the same token.
 */
export const NONDETERMINISTIC_TIME_KEYWORDS: readonly string[] = [
  "current_timestamp",
  "current_date",
  "current_time",
];

/**
 * SQLite built-ins introduced ABOVE the default contract floor (3.19.3), keyed
 * by the SQLITE_VERSION_NUMBER of the release that added them. A script that
 * calls one of these runs fine on the validator's engine and then fails on a
 * device whose SQLite predates the entry — the failure the
 * sqlite-version-too-low-for-function lint moves to authoring time by
 * comparing against the host's manifest `library.minSqliteVersionNumber`
 * (docs/validation.md).
 *
 * Every entry is sourced from the sqlite.org changelog for that release; only
 * functions with a citable release are listed (accuracy over breadth). Names
 * are compared lowercased — SQLite resolves function names case-insensitively.
 * Deliberately absent because they are AT OR BELOW the floor and therefore
 * always safe: `printf` (3.8.3), the `trim`/`ltrim`/`rtrim` family, `instr`
 * (3.7.15), `char`/`unicode` (3.8.3). Math functions are absent for the
 * opposite reason — a version gate cannot make them safe, see
 * NONPORTABLE_FUNCTIONS.
 *
 * Single-sourced here and projected into each language's generated protocol
 * constants (docs/proposals/rule-parameters-as-data.md).
 */
export const FUNCTION_MIN_VERSION: Readonly<Record<string, number>> = {
  // 3.25.0 (2018-09-15) "Add support for window functions" — the eleven
  // built-in window functions of sqlite.org/windowfunctions.html.
  row_number: 3025000,
  rank: 3025000,
  dense_rank: 3025000,
  percent_rank: 3025000,
  cume_dist: 3025000,
  ntile: 3025000,
  lag: 3025000,
  lead: 3025000,
  first_value: 3025000,
  last_value: 3025000,
  nth_value: 3025000,
  // 3.26.0 "Added PRAGMA table_xinfo". The pragma_* table-valued functions
  // are version-gated exactly like the pragmas they wrap, and appear in SQL
  // as an identifier in table position — with or without an argument list,
  // so the lint scans bare identifiers as well as identifier(…) calls.
  pragma_table_xinfo: 3026000,
  // 3.30.0 "Added PRAGMA function_list" / "PRAGMA module_list".
  pragma_function_list: 3030000,
  pragma_module_list: 3030000,
  // 3.32.0 (2020-05-22) "Added the iif() SQL function".
  iif: 3032000,
  // 3.34.0 "The substring() function is an alias for substr()". Same
  // function under a second spelling: flagging one and not the other made
  // the rule look arbitrary and left the alias a device-side crash.
  substring: 3034000,
  // 3.37.0 "Added PRAGMA table_list".
  pragma_table_list: 3037000,
  // 3.38.0 "Rename the printf() SQL function to format()" and "Added the
  // unixepoch() function". `printf` itself stays legal — it is pre-floor.
  format: 3038000,
  unixepoch: 3038000,
  // 3.41.0 (2023-02-21) "Added the unhex() SQL function".
  unhex: 3041000,
  // 3.43.0 "Added the octet_length(X) SQL function" / "Added the timediff()
  // SQL function".
  octet_length: 3043000,
  timediff: 3043000,
  // 3.44.0 "Add support for the concat() and concat_ws() scalar SQL
  // functions" / "Add support for the string_agg() aggregate SQL function".
  concat: 3044000,
  concat_ws: 3044000,
  string_agg: 3044000,
  // 3.48.0 "Added the if() SQL function as an alias for iif()". The
  // sharpest gap the table had: the identical function was an error under
  // one spelling and invisible under the other.
  if: 3048000,
  // 3.50.0 (2025-05-29) "Added the unistr() and unistr_quote() SQL
  // functions". Both, because a table that carried only one of a pair
  // released together is how every other gap in this list started.
  unistr: 3050000,
  unistr_quote: 3050000,
};

/**
 * Version floors for whole function FAMILIES, keyed by name prefix — the
 * longest matching prefix wins. Used for the JSON surface, which is far too
 * large to enumerate by hand without drift.
 *
 * The `json` entry carries a caveat worth stating: JSON1 existed as an
 * extension well before 3.38.0, but until that release it was **compile-gated**
 * (`-DSQLITE_ENABLE_JSON1`) and therefore absent from stock builds. 3.38.0 is
 * the first release where `json_*` is a built-in that is on by default, so it
 * is the first version at which a version floor alone makes the family safe —
 * that is why the whole family is treated as 3038000 rather than as its
 * historical introduction version. The `jsonb_*` family arrived with JSONB in
 * 3.45.0.
 *
 * Single-sourced here and projected per language
 * (docs/proposals/rule-parameters-as-data.md).
 */
export const FUNCTION_PREFIX_MIN_VERSION: Readonly<Record<string, number>> = {
  json: 3038000,
  jsonb: 3045000,
};

/**
 * Built-ins that a version floor can NEVER make safe, because their presence
 * is decided by the device engine's **compile options** rather than by its
 * version. The math functions arrived in 3.35.0 but sqlite.org/lang_mathfunc
 * states they "are only active if the amalgamation is compiled using the
 * -DSQLITE_ENABLE_MATH_FUNCTIONS compile-time option" — so a script calling
 * one passes validation, passes on a device whose engine happens to enable
 * them, and fails on the next device. Raising `minSqliteVersion` does not
 * help, which is why these are a separate ERROR (nonportable-function) rather
 * than a version comparison.
 *
 * `load_extension` is here for the same reason from the other direction:
 * `-DSQLITE_OMIT_LOAD_EXTENSION` removes the SQL function outright, and
 * platform builds do use it — the sqlite3 3.51.0 shipped with macOS answers
 * `SELECT load_extension('x')` with "no such function: load_extension" while
 * the JDBC driver's bundled engine compiles the same call happily. Even where
 * it is compiled in it stays disabled per connection until the host calls
 * sqlite3_enable_load_extension, so Python's stock driver answers "not
 * authorized". Three engines, three answers, one version.
 *
 * Single-sourced here and projected per language
 * (docs/proposals/rule-parameters-as-data.md).
 */
export const NONPORTABLE_FUNCTIONS: readonly string[] = [
  "acos",
  "acosh",
  "asin",
  "asinh",
  "atan",
  "atan2",
  "atanh",
  "ceil",
  "ceiling",
  "cos",
  "cosh",
  "degrees",
  "exp",
  "floor",
  "ln",
  "load_extension",
  "log",
  "log10",
  "log2",
  "mod",
  "pi",
  "pow",
  "power",
  "radians",
  "sin",
  "sinh",
  // Not math: compile-gated the same way, and for the same reason kept out
  // of FUNCTION_MIN_VERSION. `soundex` needs -DSQLITE_SOUNDEX (absent from
  // xerial's build and from macOS's system sqlite3); `sqlite_offset` needs
  // -DSQLITE_ENABLE_OFFSET_SQL_FUNC. Neither is fixable by raising a floor.
  "soundex",
  "sqlite_offset",
  "sqrt",
  "tan",
  "tanh",
  "trunc",
];

/**
 * SQLite built-in functions a script may not call at all (the
 * forbidden-function lint, docs/validation.md) — not because of a version
 * or a compile option, but because calling them does something the
 * statement denylist exists to prevent.
 *
 * `pragma_optimize` is the whole list today, and it is the reason the list
 * exists: the `pragma_*` table-valued functions are documented as ordinary
 * reads, and every one that was checked is — except this one, which
 * executes `ANALYZE`. Measured on sqlite3 3.51.0: after
 * `SELECT * FROM pragma_optimize` a database whose schema was `t,i` reads
 * `t,i,sqlite_stat1`. That is a CREATE plus a write, performed by a SELECT,
 * reaching the exact statement kind (`ANALYZE`) FORBIDDEN_LEADING_KEYWORDS
 * denies. Checked and clean, for the record: `pragma_foreign_keys(1)`
 * cannot set (max 0 args), there is no `pragma_wal_checkpoint` /
 * `pragma_shrink_memory` / `pragma_incremental_vacuum` TVF, and
 * `pragma_integrity_check` / `pragma_quick_check` are genuine reads.
 *
 * Names are compared lowercased and matched wherever the identifier
 * appears — as a call (`pragma_optimize(0xfffe)`) or bare in table position
 * (`FROM pragma_optimize`), which are both legal spellings.
 *
 * Single-sourced here and projected per language
 * (docs/proposals/rule-parameters-as-data.md).
 */
export const FORBIDDEN_FUNCTIONS: readonly string[] = ["pragma_optimize"];

/**
 * Statement kinds a script may not use, identified by the statement's FIRST
 * meaningful token (the forbidden-statement lint, docs/validation.md). Four
 * distinct hazards, all outside the script surface:
 *
 *  - transaction control (`begin`/`commit`/`end`/`rollback`/`savepoint`/
 *    `release`): the runtime's atomicity unit is the step (statements + drain).
 *    A script that opens a transaction and rolls it back erases the drain's
 *    result rows and queue updates while the host handlers have already run
 *    with real side effects — a silent-data-loss shape the run still reports
 *    as Completed.
 *  - `attach`/`detach`: the only filesystem escape. On a file-backed
 *    workspace, ATTACH gives a script read AND write access to any reachable
 *    database file.
 *  - `pragma`/`vacuum`/`analyze`/`reindex`: engine/state levers outside the
 *    contract. PRAGMA in particular can change semantics under the runtime's
 *    feet (`foreign_keys`, `recursive_triggers`, `case_sensitive_like`) or
 *    rewrite the schema outright (`writable_schema=ON`).
 *  - `explain`: a legal prefix to *any* statement, which anchored
 *    `leadingKeyword` on itself and left the real verb unread — so
 *    `EXPLAIN DELETE FROM result_get_value` bypassed protocol-table-write
 *    too. It is not inert: SQLite applies the flag pragmas
 *    (`PragTyp_FLAG`) in the code generator, i.e. during prepare, so
 *    `EXPLAIN PRAGMA writable_schema = ON` sets the flag for real while
 *    executing nothing (verified on the sqlite3 CLI 3.51.0, likewise for
 *    `foreign_keys`, `case_sensitive_like`, `recursive_triggers`,
 *    `trusted_schema`, `legacy_alter_table`, and with the
 *    `EXPLAIN QUERY PLAN` spelling). A script executes statements for
 *    effect and discards rows (docs/sqlite-surface.md §4), so EXPLAIN has
 *    no legitimate use in a payload and denying it costs nothing legal —
 *    the same argument the list already makes for `create`.
 *  - `alter`/`create`/`drop`: schema DDL. The runtime owns the workspace
 *    schema and a script has no reason to change it. `DROP TRIGGER` on a
 *    queue trigger is the sharp case — inserts into the call table then
 *    enqueue nothing, the drain finds nothing, and the run still reports
 *    Completed with zero calls executed. `ALTER TABLE … RENAME` moves a
 *    result table out from under its reader, and `CREATE TRIGGER` launders
 *    writes past the protocol-table-write rule, which only ever sees a
 *    statement's own target and never a trigger body. `script_vars` is the
 *    sanctioned scratch surface, so nothing legitimate is lost.
 *
 * Matching the FIRST token only is what keeps this precise: `pragma_table_info`
 * table-valued functions inside a SELECT, a column named `begin`, and the
 * string literal `'PRAGMA'` all remain legal, and `WITH … INSERT` is legal
 * because `with` is not on this list. Names are compared lowercased.
 *
 * Single-sourced here and projected per language
 * (docs/proposals/rule-parameters-as-data.md).
 */
export const FORBIDDEN_LEADING_KEYWORDS: readonly string[] = [
  "alter",
  "analyze",
  "attach",
  "begin",
  "commit",
  "create",
  "detach",
  "drop",
  "end",
  "explain",
  "pragma",
  "reindex",
  "release",
  "rollback",
  "savepoint",
  "vacuum",
];

/**
 * Tables SQLite owns, which a script may not write either (the
 * protocol-table-write lint, docs/validation.md). Unlike the runtime-owned
 * tables this list is FIXED rather than manifest-derived: these names are
 * SQLite's, not the host's, so nothing in a manifest can rename them and a
 * manifest-only resolution missed every one of them.
 *
 * `sqlite_master` is the sharp case and the reason this list exists: paired
 * with `PRAGMA writable_schema = ON` (reachable at prepare time through
 * `EXPLAIN`, see FORBIDDEN_LEADING_KEYWORDS) an UPDATE against it rewrites
 * the runtime's own queue trigger, after which host calls enqueue nothing
 * and the run still reports Completed. `sqlite_sequence` is the quiet one:
 * setting a table's AUTOINCREMENT high is not silent corruption but a loud
 * SQLITE_FULL on the next queue insert — still not something a script has
 * any business doing. `sqlite_stat1`..`sqlite_stat4` are the query planner's
 * statistics tables.
 *
 * Names are compared lowercased, and only as a WRITE target — reading
 * `sqlite_master` stays legal, like every other read.
 *
 * Single-sourced here and projected per language
 * (docs/proposals/rule-parameters-as-data.md).
 */
export const SYSTEM_TABLES: readonly string[] = [
  "sqlite_master",
  "sqlite_schema",
  "sqlite_sequence",
  "sqlite_stat1",
  "sqlite_stat2",
  "sqlite_stat3",
  "sqlite_stat4",
  "sqlite_temp_master",
  "sqlite_temp_schema",
];

/**
 * Binding-type compatibility: for each scalar column type, the envelope
 * binding value types (wire names) that may feed it. int64 widens from
 * int32; float64 widens from float32; integers never coerce into float
 * columns (docs/validation.md). Orthogonal to this table, a `null` binding
 * is accepted iff the column is optional — that optionality rule stays
 * inline in each consumer. Single-sourced here; consumed by the Java
 * validator (via generated Protocol.java) and the TS lint (directly).
 */
export const BINDING_TYPE_COMPAT: Record<ScalarTypeIr, readonly string[]> = {
  int32: ["int32"],
  int64: ["int32", "int64"],
  boolean: ["bool"],
  string: ["text"],
  bytes: ["blob"],
  float32: ["float32"],
  float64: ["float32", "float64"],
};

/**
 * Protocol identifier shape patterns (docs/naming.md). These use only
 * anchors and simple character classes — the common subset of JS, Java,
 * and .NET regex — so the pattern string is portable. Single-sourced here
 * and projected per language; the C# runtime deliberately ships a
 * hand-rolled char-scan instead of System.Text.RegularExpressions
 * (netstandard2.0/Unity size) and only tests equivalence against these.
 */
export const IDENTIFIER_PATTERN = "^[A-Za-z_][A-Za-z0-9_]*$";
export const METHOD_NAME_PATTERN = "^[A-Za-z][A-Za-z0-9_]*$";
export const SQL_NAME_PATTERN = "^[a-z][a-z0-9_]*$";

/** Build the column list of each runtime-managed table from the columns config. */
export function queueTableColumns(c: ColumnsIr): string[] {
  return [c.queueId, c.callId, c.method, c.status];
}
export function namedValueTableColumns(c: ColumnsIr): string[] {
  return [c.name, c.valueType, c.intValue, c.realValue, c.textValue, c.blobValue];
}
export function controlTableColumns(c: ColumnsIr): string[] {
  return [c.action, c.message];
}

/** Default per-host minimum SQLite version (the contract floor, 3.19.3). */
export const DEFAULT_MIN_SQLITE_VERSION_NUMBER = 3019003;

/** The library's own engine-verified minimum (see docs/compatibility.md for how it was established). */
export const LIBRARY_ENGINE_VERIFIED_MINIMUM = 3009000;

export const QUEUE_TABLE_V1: QueueTableIr = {
  name: "pending_host_calls",
  columns: ["queue_id", "call_id", "method", "status"],
};

export const INPUTS_TABLE_V1: InputsTableIr = {
  name: "script_inputs",
  columns: ["name", "value_type", "int_value", "real_value", "text_value", "blob_value"],
};

export const VARS_TABLE_V1: VarsTableIr = {
  name: "script_vars",
  columns: ["name", "value_type", "int_value", "real_value", "text_value", "blob_value"],
};

export const CONTROL_TABLE_V1: ControlTableIr = {
  name: "script_control",
  columns: ["action", "message"],
};
