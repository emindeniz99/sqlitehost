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
| `library` | `namespace`, `interfaceName`, `apiLevel`, `minSqliteVersionNumber`, `features` |
| `naming` | the host-level naming conventions (six prefixes/infixes + `functionPrefix`) |
| `columns` | the fourteen configurable column identifiers + done literal |
| `queueTable` | `pending_host_calls` + column list |
| `inputsTable` | `script_inputs` + column list |
| `varsTable` | `script_vars` + column list (script-managed variable scratch space) |
| `controlTable` | `script_control` + column list (halt/abort channel) |
| `scriptEnvelope` | envelope engine + binding type list |
| `methods` | ordered method descriptors (declaration order) |

## Method descriptor

`operationName` (TypeSpec op), `methodName` (protocol name),
`handlerName`, `apiLevel`, `mutates` (default true; false = inline
eligible), resolved `callTable`/`resultTable`/`queueTrigger`,
`input`/`result` shapes, and `inline` (function exposure block —
`functionName`, `minArgs`, `maxArgs`, `args`, `returns` — or null): `modelName`, scalar
`fields` (`propertyName`, `sqlName`, `column`, `scalarType`,
`optional`), and `listFields` (`propertyName`, `sqlName`, `childTable`,
`itemModelName`, `itemFields`).

All physical names in the manifest are **resolved** — consumers
(validators, DDL generators, editors) never re-derive naming. The
shared table names (`queueTable`/`inputsTable`/`varsTable`) are
configurable per host via `@hostLibrary` (see `docs/naming.md`); one
`.tsp` compilation may define **multiple** `@hostLibrary` interfaces,
each producing its own manifest and generated artifacts (each library
is an independent runtime definition with its own workspace — e.g.
dev/prod or per-screen feature APIs).

## Consumers

- Java validator: schema-aware lint + DDL generation from the manifest.
- TypeScript authoring: autocomplete metadata + static lint.
- Cross-language goldens: emitter output must equal the committed
  snapshot byte-for-byte.
