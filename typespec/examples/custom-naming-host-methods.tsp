// A fourth, conformance-only host: every configurable name overridden.
//
// It exists because all three DDL generators (codegen/core/src/ddl.ts,
// Java's DdlGenerator, C#'s SchemaGenerator) were pinned only against
// hosts that take the protocol defaults. Each generator carries its own
// copy of the naming rules, so a copy that ignored `inputColumnPrefix`
// and hardcoded "input_" — or read `queueTable` from the wrong manifest
// block — produced byte-identical output on every committed fixture. The
// goldens could not see it, because the right answer and the wrong one
// were the same string.
//
// Nothing here is a name anyone should copy. The point is that no value
// equals its default: prefixes, infixes, the four shared workspace
// tables, all fourteen shared columns, the done-status literal and the
// inline function prefix. `@sqlName` names one column, one child table
// (via the list field) and one item column against the grain of the
// property name, so the emitters cannot pass by deriving from the
// property instead of reading the manifest.
//
// Nothing generates C#, Java or TypeScript from it — manifest and DDL
// only, pinned in tests/cross-language-golden/run.mjs and read back by
// the Java and C# DDL golden tests.
import "@sqlite-host/typespec";

using SqliteHost;

namespace Example.CustomNaming;

@hostLibrary({
  apiLevel: 1,
  minSqliteVersion: "3.19.3",
  callTablePrefix: "req_",
  resultTablePrefix: "resp_",
  inputColumnPrefix: "arg_",
  resultColumnPrefix: "ret_",
  inputListTableInfix: "__arg_",
  resultListTableInfix: "__ret_",
  functionPrefix: "hostfn_",
  queueTable: "host_queue",
  inputsTable: "host_inputs",
  varsTable: "host_scratch",
  controlTable: "host_control",
  callIdColumn: "correlation_id",
  itemIndexColumn: "ordinal",
  statusColumn: "state",
  doneStatusValue: "settled",
  queueIdColumn: "seq",
  methodColumn: "op_name",
  nameColumn: "var_name",
  valueTypeColumn: "kind",
  intValueColumn: "i_val",
  realValueColumn: "r_val",
  textValueColumn: "t_val",
  blobValueColumn: "b_val",
  actionColumn: "verb",
  messageColumn: "detail"
})
interface CustomNamingHostMethods {
  @hostMethod({
    name: "lookup",
    handler: "Lookup",
    mutates: false
  })
  op Lookup(input: LookupInput): LookupResult;

  @hostMethod({
    name: "bulkLookup",
    handler: "BulkLookup"
  })
  op BulkLookup(input: BulkLookupInput): BulkLookupResult;
}

model LookupInput {
  // The column keeps a legacy spelling the property name would never
  // derive, which is the case a generator that re-derives from the
  // property instead of reading the manifest gets wrong.
  @sqlName("lookup_key")
  key: string;
}

model LookupResult {
  @sqlName("payload")
  value: int64;
}

model BulkLookupInput {
  // Names the child table: req_bulk_lookup__arg_wanted_keys.
  @sqlName("wanted_keys")
  keys: LookupKeyItem[];
}

model BulkLookupResult {
  @sqlName("found_rows")
  rows: LookupRowItem[];
}

model LookupKeyItem {
  @sqlName("lookup_key")
  key: string;
}

model LookupRowItem {
  @sqlName("lookup_key")
  key: string;

  @sqlName("payload")
  value: int64;

  @sqlName("was_found")
  found: boolean;
}
