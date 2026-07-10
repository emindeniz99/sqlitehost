# API levels and compatibility

Each generated method spec carries an API level; the host definition
carries the library's supported API level. Payloads declare
`requiredApiLevel`, `requiredFeatures`, `requiredMethods`.

## Runtime behavior (clean skip)

```text
payload requiredApiLevel > runtime supportedApiLevel  -> SkippedUnsupported
required feature missing                              -> SkippedUnsupported
required method missing                               -> SkippedUnsupported
```

A clean skip never opens the workspace, executes SQL, or invokes
handlers. Error codes: `unsupported-api-level`, `missing-feature`,
`missing-method`, `unsupported-engine` (see `docs/errors.md`).

## Protocol v1 features

```text
typedNamedBindings   typed named parameters (:name / @name / $name)
splitResultTables    per-method result_<method> tables with status column
scriptInputs         runtime inputs via the script_inputs table
scriptVars           script-managed variable scratch table (script_vars)
scriptControl        early halt/abort via the script_control table
```

## Breaking method contract changes

```text
create a new method name
assign a higher API level
keep the old method while old scripts/clients may still use it
```

No signature-version subsystem in v1.
