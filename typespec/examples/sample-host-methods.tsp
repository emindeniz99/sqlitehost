import "@sqlite-host/typespec";

using SqliteHost;

namespace Example.Game;

@hostLibrary({
  apiLevel: 1,
  callTablePrefix: "call_",
  resultTablePrefix: "result_",
  inputColumnPrefix: "input_",
  resultColumnPrefix: "result_",
  inputListTableInfix: "__input_",
  resultListTableInfix: "__result_"
})
interface GameHostMethods {
  @hostMethod({
    name: "getValue",
    handler: "GetValue"
  })
  op GetValue(input: GetValueInput): GetValueResult;

  @hostMethod({
    name: "setValue",
    handler: "SetValue"
  })
  op SetValue(input: SetValueInput): SetValueResult;

  @hostMethod({
    name: "getValues",
    handler: "GetValues"
  })
  op GetValues(input: GetValuesInput): GetValuesResult;

  @hostMethod({
    name: "putBlob",
    handler: "PutBlob"
  })
  op PutBlob(input: PutBlobInput): PutBlobResult;

  @hostMethod({
    name: "recordScore",
    handler: "RecordScore"
  })
  op RecordScore(input: RecordScoreInput): RecordScoreResult;
}

model GetValueInput {
  @sqlName("key")
  key: string;
}

model GetValueResult {
  @sqlName("value")
  value: int64;
}

model SetValueInput {
  @sqlName("key")
  key: string;

  @sqlName("value")
  value: int64;
}

model SetValueResult {
  @sqlName("success")
  success: boolean;
}

model GetValuesInput {
  @sqlName("default_value")
  defaultValue?: int64;

  @sqlName("keys")
  keys: KeyQueryItem[];
}

model GetValuesResult {
  @sqlName("entries")
  entries: ValueEntryItem[];
}

model KeyQueryItem {
  @sqlName("key")
  key: string;
}

model ValueEntryItem {
  @sqlName("key")
  key: string;

  @sqlName("value")
  value: int64;

  @sqlName("found")
  found: boolean;
}

model PutBlobInput {
  @sqlName("key")
  key: string;

  @sqlName("data")
  data: bytes;

  @sqlName("note")
  note?: string;
}

model PutBlobResult {
  @sqlName("stored")
  stored: boolean;
}

model RecordScoreInput {
  @sqlName("key")
  key: string;

  @sqlName("score")
  score: float64;

  @sqlName("weight")
  weight?: float32;
}

model RecordScoreResult {
  @sqlName("average")
  average: float64;
}
