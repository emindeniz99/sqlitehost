// A third, conformance-only host: identical in shape to the smallest
// useful slice of the sample host, but declaring a RAISED SQLite floor
// (3.39.0 rather than the default 3.19.3).
//
// It exists because sqlite-version-too-low-for-syntax has two halves to
// prove and the corpus could only express one of them. Every committed
// manifest declared 3.19.3, so a payload using above-floor syntax could
// only ever be an error case; there was no way to pin the other direction
// — that raising `minSqliteVersion`, the documented unlock path
// (docs/sqlite-surface.md), actually silences the rule in BOTH validators.
// A detector nobody can satisfy is a ban, not a version check.
//
// 3.39.0 is the newest version any SYNTAX_MIN_VERSION entry names, so one
// host clears every detector at once. Referenced from
// fixtures/payloads/expectations.json through the per-case `manifest` key.
// Nothing generates C#, Java or TypeScript from it.
import "@sqlite-host/typespec";

using SqliteHost;

namespace Example.SyntaxFloor;

@hostLibrary({
  apiLevel: 1,
  minSqliteVersion: "3.39.0",
  callTablePrefix: "call_",
  resultTablePrefix: "result_",
  inputColumnPrefix: "input_",
  resultColumnPrefix: "result_",
  inputListTableInfix: "__input_",
  resultListTableInfix: "__result_"
})
interface SyntaxFloorHostMethods {
  @hostMethod({
    name: "getValue",
    handler: "GetValue",
    mutates: false
  })
  op GetValue(input: GetValueInput): GetValueResult;
}

model GetValueInput {
  @sqlName("key")
  key: string;
}

model GetValueResult {
  @sqlName("value")
  value: int64;
}
