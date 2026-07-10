# Canonical manifest

The manifest is the serialized IR (`codegen/core/src/ir.ts`) — the
neutral artifact every language is tested against. Canonical bytes:
pinned key order (as in `codegen/core/src/manifest.ts`), 2-space
indent, LF, trailing newline. Committed snapshot:
`fixtures/manifests/sample-host.manifest.json`.

## Top-level keys (in order)

| Key | Content |
|---|---|
| `manifestVersion` | `1` |
| `engine` | `"sqlite-host-v1"` |
| `library` | `namespace`, `interfaceName`, `apiLevel`, `features` |
| `naming` | the six host-level naming conventions |
| `queueTable` | `pending_host_calls` + column list |
| `inputsTable` | `script_inputs` + column list |
| `scriptEnvelope` | envelope engine + binding type list |
| `methods` | ordered method descriptors (declaration order) |

## Method descriptor

`operationName` (TypeSpec op), `methodName` (protocol name),
`handlerName`, `apiLevel`, resolved `callTable`/`resultTable`/
`queueTrigger`, and `input`/`result` shapes: `modelName`, scalar
`fields` (`propertyName`, `sqlName`, `column`, `scalarType`,
`optional`), and `listFields` (`propertyName`, `sqlName`, `childTable`,
`itemModelName`, `itemFields`).

All physical names in the manifest are **resolved** — consumers
(validators, DDL generators, editors) never re-derive naming.

## Consumers

- Java validator: schema-aware lint + DDL generation from the manifest.
- TypeScript authoring: autocomplete metadata + static lint.
- Cross-language goldens: emitter output must equal the committed
  snapshot byte-for-byte.
