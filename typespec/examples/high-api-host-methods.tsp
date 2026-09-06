// A second, deliberately tiny host: one method whose apiLevel sits above
// the lowest a script may declare. The sample host's methods are all
// apiLevel 1 and the envelope rejects requiredApiLevel < 1, so no payload
// against that manifest can under-declare its API level — which left
// `method-api-level-too-high` as the one lint code with no conformance
// fixture (docs/validation.md). This host exists to give it one, and is
// referenced by fixtures/payloads/expectations.json through the per-case
// `manifest` key. Nothing generates C#, Java or TypeScript from it.
import "@sqlite-host/typespec";

using SqliteHost;

namespace Example.HighApi;

@hostLibrary({
  apiLevel: 2,
  minSqliteVersion: "3.19.3",
  callTablePrefix: "call_",
  resultTablePrefix: "result_",
  inputColumnPrefix: "input_",
  resultColumnPrefix: "result_",
  inputListTableInfix: "__input_",
  resultListTableInfix: "__result_"
})
interface HighApiHostMethods {
  @hostMethod({
    name: "newFeature",
    handler: "NewFeature",
    apiLevel: 2
  })
  op NewFeature(input: NewFeatureInput): NewFeatureResult;
}

model NewFeatureInput {
  @sqlName("key")
  key: string;
}

model NewFeatureResult {
  @sqlName("ok")
  ok: boolean;
}
