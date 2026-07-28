package io.sqlitehost.validator;

/**
 * The pinned lint codes (docs/validation.md — asserted by
 * fixtures/payloads/expectations.json). These strings are stable
 * across releases.
 */
public final class ValidationCodes {

    // Structural
    public static final String INVALID_ENVELOPE = "invalid-envelope";
    public static final String DUPLICATE_STEP_ID = "duplicate-step-id";
    public static final String REQUIRED_API_LEVEL_TOO_HIGH = "required-api-level-too-high";
    public static final String METHOD_API_LEVEL_TOO_HIGH = "method-api-level-too-high";
    public static final String UNKNOWN_REQUIRED_FEATURE = "unknown-required-feature";
    public static final String UNKNOWN_REQUIRED_METHOD = "unknown-required-method";
    public static final String DUPLICATE_INPUT_NAME = "duplicate-input-name";

    // Bindings
    public static final String MISSING_BINDING = "missing-binding";
    public static final String UNUSED_BINDING = "unused-binding";
    public static final String BINDING_TYPE_MISMATCH = "binding-type-mismatch";
    public static final String MIXED_PREFIX_BINDING = "mixed-prefix-binding";
    public static final String POSITIONAL_PARAMETER = "positional-parameter";

    // Host-call usage
    public static final String IMPLICIT_COLUMN_LIST = "implicit-column-list";
    public static final String UNDECLARED_METHOD_USE = "undeclared-method-use";
    public static final String UNUSED_REQUIRED_METHOD = "unused-required-method";
    public static final String DUPLICATE_CALL_ID = "duplicate-call-id";
    public static final String LIST_CHILD_LATER_STEP = "list-child-later-step";
    public static final String LIST_CHILD_WITHOUT_PARENT = "list-child-without-parent";

    // Inline functions (feature inlineFunctions)
    public static final String UNDECLARED_FEATURE_USE = "undeclared-feature-use";
    public static final String UNKNOWN_FUNCTION = "unknown-function";
    public static final String FUNCTION_ARITY_MISMATCH = "function-arity-mismatch";

    // Determinism
    public static final String NONDETERMINISTIC_FUNCTION = "nondeterministic-function";

    // Result-read lineage
    public static final String RESULT_READ_UNKNOWN_CALL = "result-read-unknown-call";
    public static final String RESULT_READ_NOT_AFTER_CALL = "result-read-not-after-call";

    // Prepare-only SQLite validation (layer 3, sqlite-host-jdbc)
    public static final String SQL_PREPARE_ERROR = "sql-prepare-error";

    private ValidationCodes() {
    }
}
