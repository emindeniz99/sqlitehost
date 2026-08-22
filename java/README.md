# SqliteHost Java

Java modules for SqliteHost script payloads (see `../docs/`):

- **sqlite-host-model** — envelope + manifest model, strict JSON
  reader/writer, canonical DDL generator.
- **sqlite-host-validator** — script semantic lint (structural,
  bindings, host-call usage, result-read lineage) with a thin CLI.
- **sqlite-host-jdbc** — prepare-only SQLite validation over the
  generated schema.

## Build and test

```sh
cd java
mvn -q test      # run all module tests
mvn -q package   # also builds the validator CLI fat jar
```

## Validator CLI

`mvn -q package` shades an executable fat jar (the semantic lint
engine plus its Jackson dependency) at:

```
sqlite-host-validator/target/sqlite-host-validator-<version>-cli.jar
```

Run it via the launcher script or `java -jar` directly:

```sh
bin/sqlite-host-validate <manifest.json> <script.json>

java -jar sqlite-host-validator/target/sqlite-host-validator-0.1.0-cli.jar \
    ../fixtures/manifests/sample-host.manifest.json \
    ../fixtures/payloads/valid/example-006-floats.json
```

One finding is printed per line. Exit codes:

| Code | Meaning |
|---|---|
| 0 | script is publishable (no errors; warnings don't block) |
| 1 | script has validation errors |
| 2 | usage error, or the manifest/script could not be read |

Note: the CLI runs the semantic lint only. Prepare-only SQLite
validation (`sql-prepare-error`, docs/validation.md layer 3) lives in
`sqlite-host-jdbc` and is exercised by the conformance tests.
