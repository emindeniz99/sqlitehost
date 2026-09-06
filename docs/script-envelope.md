# Script envelope contract (protocol v1)

The script envelope is the cross-language payload contract. It is
defined in TypeSpec (`typespec/library`) and projected into C#
(`SqliteHost.Abstractions`), Java (`sqlite-host-model`), and TypeScript
(`@sqlite-host/runtime-types`). The JSON shape below is normative;
golden tests keep the three projections in sync.

## Shape

```json
{
  "engine": "sqlite-host-v1",
  "scriptId": "example-001",
  "requiredApiLevel": 1,
  "requiredFeatures": ["typedNamedBindings", "splitResultTables"],
  "requiredMethods": ["getValue", "setValue"],
  "inputs": [
    { "name": "targetValue", "value": { "type": "int64", "value": 42 } }
  ],
  "steps": [
    {
      "id": "read-current",
      "statements": [
        {
          "sql": "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'example-key')",
          "bindings": {
            "callId": { "type": "text", "value": "read-1" }
          }
        }
      ]
    }
  ]
}
```

| Field | Required | Notes |
|---|---|---|
| `engine` | yes | must be `"sqlite-host-v1"` |
| `scriptId` | no | opaque identifier for diagnostics |
| `requiredApiLevel` | yes | integer ≥ 1 |
| `requiredFeatures` | no | subset of the host's supported features, else clean skip |
| `requiredMethods` | no | methods the script uses; missing method → clean skip |
| `inputs` | no | runtime inputs inserted into `script_inputs` before step 1; names must be unique (`duplicate-input-name`); SqliteHost never computes or injects runtime facts itself — the caller places them in `inputs` before `Run(script)` |
| `steps` | yes | ordered; step `id`s must be unique and non-blank |
| `steps[].statements` | yes | ordered, non-empty; each has `sql` and optional `bindings` |

## Binding values

Discriminated by `type`:

| `type` | JSON `value` | SQLite storage |
|---|---|---|
| `null` | absent | NULL |
| `int32` | number (or decimal string) in int32 range | INTEGER |
| `int64` | number when \|v\| ≤ 2^53−1, else decimal string; parsers accept both | INTEGER |
| `bool` | `true` / `false` | INTEGER 1 / 0 |
| `text` | string | TEXT |
| `blob` | canonical base64 string (standard alphabet, padding, no line breaks, padding bits zero) | BLOB |
| `float32` | finite JSON number representable as an IEEE-754 single (parsed via round-to-nearest); string form NOT accepted | REAL |
| `float64` | finite JSON number; string form NOT accepted | REAL |

Float rules: NaN and ±Infinity are not representable (JSON has no
literal for them) and readers must reject any string-typed value for
`float32`/`float64` — unlike `int64`, floats never need a string form
because every IEEE-754 double round-trips through a JSON number.

**Canonical float text** is what ECMAScript's `Number::toString`
produces, which is what `JSON.stringify` writes in
`@sqlite-host/runtime-types`: the shortest digits that round-trip the
double, plain decimal notation while the decimal exponent `n` satisfies
`-6 < n ≤ 21` and `d.ddde±XX` otherwise (exponent always signed, never
zero-padded), `-0` spelled `0`, and no `.0` tail on an integral value —
so `1e23` is `1e+23`, `1e-6` is `0.000001`, and `3.0` is `3`. A
`float32` is written through the double it widens to, which is the
single the engine will store. Writers must not delegate this to the
platform's own double formatter: Java's `Double.toString` spells the
same values `9.999999999999999E22`, `1.0E-6` and `3.0`, and changed its
digit selection in JDK 19 (JDK-4511638), so an envelope written on one
JDK would not match the same envelope written on another. `example-018`
in `fixtures/payloads/valid` is the pinned corpus for these spellings.

Base64 must be **canonical**: `"QR=="` is refused even though it decodes
to the same byte as `"QQ=="`, because the four bits it carries past that
byte are padding and must be zero. Several spellings of one blob would
force a reader to choose which to re-emit, and an envelope is signed
bytes — normalizing after verification produces a different artifact from
the one that was signed.

A required string must be **non-blank**, not merely non-empty: `"   "` is
rejected wherever `""` is (step `id`, statement `sql`, input `name`,
and each key of a statement's `bindings` map).
Blankness is decided on one pinned character set — space, `\t`, `\n`,
`\v`, `\f`, `\r`, C's `isspace()`, the same set the SQL scanners share —
rather than each language's own idea of whitespace, which differ.

An **explicit JSON `null` is not an absent field.** Every optional field
above is absent by being missing from the object; spelling it `null`
instead is a type error and readers reject the payload. This is the same
rule the `null` binding type already states from the other side — a
`{"type": "null", "value": null}` is refused because a present `value` is
present even when it is null. Serializers that emit `null` for an absent
optional must be configured not to; an envelope is signed bytes, and a
reader that quietly accepted both spellings would let the same payload be
publishable through one SDK and not another.

An **object may not repeat a key**, and a reader must reject a payload
that does — it may not resolve the collision. Last-wins is what
`JSON.parse`, Jackson's `readTree` and `System.Text.Json`'s
`JsonDocument` all happen to do, but it is a convention, not a rule of
JSON: keeping the first value is equally conforming, and .NET's own
`JsonNode.Parse` throws. This document pins every other
canonicalization corner for one reason — the validator must judge the
same document the device runs — and a repeated key is precisely a
document two conforming readers may read differently.
`{"sql": "ATTACH …", "sql": "SELECT 1"}` is that bypass with no
tampering anywhere. Rejecting is the only resolution that cannot differ
between implementations, which is why it is the rule rather than a
pinned last-wins.

Binding **names** are bare (no prefix). In SQL, named parameters may be
written `:name`, `@name`, or `$name`; a binding matches a parameter when
the names are equal after stripping the prefix character. One binding
may feed the same name through several prefix forms in one statement
(supported; validators warn with `mixed-prefix-binding`) — prefer
`:name` consistently. Positional (`?`) parameters are not supported in
v1.

## Semantics

- Statements run in order within a step; steps run in order.
- The runtime drains `pending_host_calls` only after **all** statements
  in a step succeeded — never between statements of the same step.
  Result rows for calls emitted in step N are therefore visible to SQL
  starting at step N+1.
- Parent call rows and their list child rows must be emitted in the
  same step (see `docs/validation.md`, `list-child-later-step`).
- JSON parsing is not part of the core C# runtime: the runtime consumes
  a parsed `SqliteHostScript` object. Java (`sqlite-host-model`) and
  TypeScript (`@sqlite-host/runtime-types`) provide JSON parsing for
  tooling/validation. A C# JSON helper package was considered and
  declined (ROADMAP.md, "Dropped") — bring your own serializer.
