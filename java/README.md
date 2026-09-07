# SqliteHost Java

Java modules for SqliteHost script payloads (see `../docs/`):

- **sqlite-host-model** — envelope + manifest model, strict JSON
  reader/writer, canonical DDL generator.
- **sqlite-host-validator** — script semantic lint (structural,
  bindings, host-call usage, result-read lineage).
- **sqlite-host-jdbc** — prepare-only SQLite validation over the
  generated schema, plus the validator CLI.

## Build and test

```sh
cd java
mvn -q test      # run all module tests
mvn -q package   # also builds the validator CLI fat jar
```

## Validator CLI

`mvn -q package` shades an executable fat jar at:

```
sqlite-host-jdbc/target/sqlite-host-jdbc-<version>-cli.jar
```

Run it with `java -jar`:

```sh
java -jar sqlite-host-jdbc/target/sqlite-host-jdbc-0.1.0-cli.jar \
    <manifest.json> <script.json>

java -jar sqlite-host-jdbc/target/sqlite-host-jdbc-0.1.0-cli.jar \
    ../fixtures/manifests/sample-host.manifest.json \
    ../fixtures/payloads/valid/example-006-floats.json
```

One finding is printed per line. Exit codes:

| Code | Meaning |
|---|---|
| 0 | script is publishable (no errors; warnings don't block) |
| 1 | script has validation errors — including a script the strict reader refuses, which prints an `invalid-envelope` finding rather than dying as a tooling failure |
| 2 | no verdict was reached: wrong argument count, either file unreadable, a manifest that will not parse, or a workspace layer 3 could not set up |

**The CLI runs all four validation layers**, prepare-only SQLite
(`sql-prepare-error`, `docs/validation.md` layer 3) included. That is why
it lives in `sqlite-host-jdbc` and not in `sqlite-host-validator`: layer
3 is here, this module already depends on the lint engine, and putting
the CLI on the other side would cycle the module graph. While it ran the
lint alone it exited 0 — silently — on
`fixtures/payloads/invalid/unknown-column.json`, a payload the
conformance corpus calls invalid.

The cost of that is the jar: it bundles the xerial driver and its native
libraries, so the fat jar is about 14 MB rather than about 2 MB. It is a
local tool run once per payload in a pipeline, not something shipped to
a device, so size is the cheap side of the trade. The library jars are
unaffected.
