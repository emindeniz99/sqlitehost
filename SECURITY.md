# Security Policy

SqliteHost executes SQL scripts your backend authored against a temporary
SQLite workspace, and turns rows in `call_*` tables into typed host-method
calls. Two parts of that are security-sensitive: the signed-envelope
verification in `SqliteHost.Delivery`, which decides whether a downloaded
script is the one you signed, and the parameter binding that keeps payload
values out of SQL text.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting:
<https://github.com/emindeniz99/sqlitehost/security/advisories/new>

That link 404s if the repository setting has not been switched on yet. If
it does, email <emindeniz99@gmail.com> with `sqlitehost` in the subject —
do not fall back to a public issue.

Please do not open a public issue for anything exploitable. Expect a first
response within a week.

## Supported versions

| Version | Supported |
|---|---|
| `main` | yes |
| everything else | no releases yet |

The project is pre-1.0 and nothing has been released, so report against
`main`. Once releases start, fixes will ship on the newest one across
every artifact at once, with no backports.

## Artifacts

Nothing is published yet: these are the planned artifact ids, and the
registry accounts do not exist (`docs/packaging.md`, ROADMAP.md).

| Registry | Package |
|---|---|
| npm | `@sqlite-host/typespec`, `@sqlite-host/runtime-types`, `@sqlite-host/authoring` |
| NuGet | `SqliteHost.Abstractions`, `SqliteHost.Runtime`, `SqliteHost.Conformance`, `SqliteHost.Adapters.Native`, `SqliteHost.Delivery` |
| Maven Central | `io.github.emindeniz99:sqlite-host-model`, `:sqlite-host-validator`, `:sqlite-host-jdbc` |
| UPM | `com.sqlitehost.runtime` |

Once they ship, all of them will be built from one tag in this
repository, and a report against any of them belongs here.

## Threat model

The design assumes **your backend authors the scripts and the validators
gate them before publication**. It does not assume arbitrary third parties
uploading code. Two consequences worth stating before you file:

- The statement denylist and the engine-portability rules live in the Java
  validator and the TypeScript lint only. The C# runtime and the Unity
  package contain none of them, deliberately, because the binary budget is
  what the project exists to protect. A payload that never passed the
  validator is outside the design (`docs/validation.md`).
- Full SQL sandboxing is a v1 non-goal, and SqliteHost never claims to be
  one. A hostile author who controls both the payload and the delivery path
  is out of scope (`docs/why-sql-not-a-vm.md`).

## What counts

- **Verification bypass in `SqliteHost.Delivery`**: a forged, expired,
  tampered, algorithm-substituted, or wrong-key envelope that verifies.
  The committed counterexamples are in `fixtures/delivery/`; a case that
  slips past them is exactly what we want to hear about.
- **Binding escape**: a payload value that reaches SQL text instead of a
  bound parameter, in any adapter.
- **Cross-language differential**: two languages disagreeing on the same
  manifest, DDL, or envelope. One of them is wrong, and the goldens missed
  it.
- **Adapter contract violations that fail silently.** Swallowing an error
  is itself a contract violation (`docs/adapter-contract.md`); the
  conformance suite exists to catch it.
- **Memory safety in `SqliteHost.Adapters.Native`**: it calls libsqlite3
  through `DllImport`, so a crash or corruption on malformed input is a
  real finding.
- **Supply chain**: a compromised or unpinned step in the release
  workflows, or a published artifact whose contents do not match the tag.

## What does not count

- A script that runs forever. Recursive CTEs are unbounded by SQLite, and
  the runtime does not bound them. This is documented, not a defect.
- Behaviour of a payload that skipped the validator, per the threat model
  above.
- `fixtures/delivery/*.insecure-private.pem`. Those are throwaway
  development keys committed on purpose so the delivery goldens can be
  regenerated. They protect nothing.
- Features above the SQLite 3.19.3 floor failing on an old engine. That is
  the documented floor (`docs/sqlite-surface.md`).
