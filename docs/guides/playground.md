# Playground — try a host definition in the browser

The playground is a single static page that runs the whole SqliteHost
frontend locally: you type a `.tsp` host definition, and the manifest,
schema DDL, and generated C#, Java, and TypeScript appear as you type.
Nothing is uploaded — the TypeSpec compiler and every emitter are
bundled into the page.

It exists for the "what would this generate?" question that otherwise
costs a checkout, `pnpm install`, and an emitter run: evaluating
SqliteHost, sketching a host contract before committing to it, or
checking what a naming option does to the emitted tables.

## Run it

```bash
cd <repo root>
pnpm install
pnpm --dir typescript/playground run build:web
python3 -m http.server --directory typescript/playground/web-dist
```

Then open the printed URL. The build output is not committed (see
[typescript/playground/README.md](../../typescript/playground/README.md)),
so the build step is required once.

## Using the page

The editor starts on `typespec/examples/sample-host-methods.tsp`, the
same sample the golden tests pin. Edit it and the page recompiles after
400 ms of quiet typing.

| Tab | Shows |
|---|---|
| Manifest | the canonical manifest JSON ([docs/manifest.md](../manifest.md)) |
| DDL | the workspace schema snapshot ([docs/workspace-schema.md](../workspace-schema.md)) |
| C# | generated sources, with a selector for the `classic` / `compact` / `ultra` size profiles ([docs/csharp-api.md](../csharp-api.md)) |
| Java | generated envelope model, DTOs, and method descriptors |
| TypeScript | generated envelope types and per-host authoring module |

Tabs that emit more than one file get a file selector. The panel below
the output lists every diagnostic — TypeSpec compile errors and
SqliteHost model-validation errors alike ([docs/validation.md](../validation.md))
— with `line:column` positions into your source.

## What it is not

The playground compiles and emits; it does not execute. Running a
script payload against a SQLite workspace needs the C# runtime or the
Java validator. For linting a payload against a manifest in the
browser, see the admin demo in `typescript/sample-admin`.

Output is byte-identical to what the CLI emitters write for the same
source — the playground uses the same frontend and the same emitters,
differing only in where the compiler reads files from. That equivalence
is a pinned test, not an aspiration: `typescript/playground/src/test/parity.test.ts`
compares its output against the same committed goldens
`tests/cross-language-golden/run.mjs` uses.

The page itself is pinned too. `typescript/playground/e2e/` drives the
built page in a real Chromium (Playwright): it asserts that the loaded
page requests nothing but its own two files, that the Manifest tab
renders the committed golden byte for byte, and that the tabs, the C#
profile selector, and the debounced recompile behave as described above.
Run it with `pnpm --dir typescript/playground run test:e2e` after a
`build:web`; `tests/end-to-end/run-all.sh` includes it.
