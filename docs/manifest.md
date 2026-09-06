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
| `columns` | fourteen keys: thirteen configurable column identifiers plus `doneValue`, the done literal |
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

## Loading a manifest

`parseManifest` (`codegen/core/src/manifest.ts`) validates structure
before returning an IR, and every emitter CLI funnels through it: keys
present and typed, no unknown top-level keys, `manifestVersion` 1, a
positive integral `apiLevel` per library and per method with no method
above its library's level, `minArgs <= maxArgs <= args.length`, unique
method names, unique table names (compared lowercased, as SQLite
resolves them) and unique `sqlName`s within a shape. Problems are
reported together, each with its JSON path, because a hand-edited or
merge-conflicted manifest rarely has just one.

What it deliberately does **not** check is whether a resolved name is
what the naming conventions would derive. A manifest carries resolved
names precisely so a host can keep a legacy table or column name.
`parseManifestUnchecked` skips the whole check and exists for test
fixtures that build deliberately non-conforming IRs.

## Consumers

- Java validator: schema-aware lint + DDL generation from the manifest.
- TypeScript authoring: autocomplete metadata + static lint.
- Cross-language goldens: emitter output must equal the committed
  snapshot byte-for-byte.
