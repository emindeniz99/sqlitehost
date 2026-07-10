// Generic script envelope contract (protocol v1, plan §9.1). These
// models are the documented source of truth for the cross-language
// payload: C# (SqliteHost.Abstractions), Java (sqlite-host-model), and
// TypeScript (@sqlite-host/runtime-types) each ship a vendored
// generated projection of this shape (see docs/script-envelope.md).

namespace SqliteHost.Protocol;

model Script {
  engine: string;
  scriptId?: string;
  requiredApiLevel: int32;
  requiredFeatures?: string[];
  requiredMethods?: string[];
  inputs?: RuntimeInput[];
  steps: Step[];
}

model RuntimeInput {
  name: string;
  value: BindingValue;
}

model Step {
  id: string;
  statements: Statement[];
}

model Statement {
  sql: string;
  bindings?: Record<BindingValue>;
}

union BindingValue {
  nullValue: NullBinding,
  int32Value: Int32Binding,
  int64Value: Int64Binding,
  boolValue: BoolBinding,
  textValue: TextBinding,
  blobValue: BlobBinding,
  float32Value: Float32Binding,
  float64Value: Float64Binding,
}

model NullBinding {
  type: "null";
}

model Int32Binding {
  type: "int32";
  value: int32;
}

model Int64Binding {
  type: "int64";
  value: int64;
}

model BoolBinding {
  type: "bool";
  value: boolean;
}

model TextBinding {
  type: "text";
  value: string;
}

model BlobBinding {
  type: "blob";
  value: bytes;
}

model Float32Binding {
  type: "float32";
  value: float32;
}

model Float64Binding {
  type: "float64";
  value: float64;
}
