using TypeSpec.Reflection;

namespace SqliteHost;

/**
 * Options for `@hostLibrary`. `apiLevel` is required; the six naming
 * keys are optional and default to the protocol v1 naming conventions
 * (see docs/naming.md). `minSqliteVersion` is the minimum SQLite
 * version required by the host library, as a dotted version string
 * (e.g. "3.19.3" or "3.8.11.1"); it defaults to 3.19.3 when absent.
 * The four shared workspace table names default to the protocol v1
 * names (`pending_host_calls`, `script_inputs`, `script_vars`,
 * `script_control`); they must be non-empty, mutually distinct, and
 * must not collide with any derived call/result/child table name.
 * The fourteen column options (thirteen `...Column` identifiers plus
 * the `doneStatusValue` literal) default to the protocol v1 names in
 * docs/naming.md; column names must be non-empty, mutually distinct
 * within each table's column set, and the row-identity columns
 * (callId/itemIndex/status) must not collide with any derived
 * input/result field column.
 * `functionPrefix` (default `fn_`) prefixes derived inline scalar
 * function names (`fn_` + snake(methodName), docs/naming.md); it must
 * be non-empty but, being a prefix rather than a table, joins no other
 * distinctness check.
 */
model HostLibraryOptions {
  apiLevel: int32;
  minSqliteVersion?: string;
  callTablePrefix?: string;
  resultTablePrefix?: string;
  inputColumnPrefix?: string;
  resultColumnPrefix?: string;
  inputListTableInfix?: string;
  resultListTableInfix?: string;
  functionPrefix?: string;
  queueTable?: string;
  inputsTable?: string;
  varsTable?: string;
  controlTable?: string;
  callIdColumn?: string;
  itemIndexColumn?: string;
  statusColumn?: string;
  doneStatusValue?: string;
  queueIdColumn?: string;
  methodColumn?: string;
  nameColumn?: string;
  valueTypeColumn?: string;
  intValueColumn?: string;
  realValueColumn?: string;
  textValueColumn?: string;
  blobValueColumn?: string;
  actionColumn?: string;
  messageColumn?: string;
}

/**
 * Options for `@hostMethod`. `name` is the logical protocol method name
 * (e.g. "getValue"); `handler` is the member name on the generated
 * handler interface. `apiLevel` defaults to the library's api level.
 * `mutates` (default `true`) declares whether the handler mutates host
 * state; only `mutates: false` methods are eligible for inline scalar
 * function exposure (docs/proposals/inline-host-functions.md). Every
 * eligible method is exposed automatically; `inline: false` opts a
 * method out, `inline: true` (or setting `functionName`) forces the
 * attempt and turns ineligibility into compile errors. `functionName`
 * overrides the derived `functionPrefix + snake(name)` function name.
 */
model HostMethodOptions {
  name: string;
  handler: string;
  apiLevel?: int32;
  mutates?: boolean;
  inline?: boolean;
  functionName?: string;
}

/**
 * Marks an interface as a SqliteHost host library. Every operation in
 * the interface must carry `@hostMethod`.
 */
extern dec hostLibrary(target: Interface, options: HostLibraryOptions);

/**
 * Declares a host method binding for an operation inside a
 * `@hostLibrary` interface.
 */
extern dec hostMethod(target: Operation, options: HostMethodOptions);

/**
 * Overrides the logical SQL name of a model property. Defaults to
 * snake_case of the property name when absent.
 */
extern dec sqlName(target: ModelProperty, name: valueof string);
