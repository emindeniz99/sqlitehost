package io.sqlitehost.model.manifest;

/**
 * IR scalar type (manifest {@code scalarType} values). Mirrors
 * {@code ScalarTypeIr} in codegen/core/src/ir.ts.
 */
public enum ScalarType {
    INT32("int32"),
    INT64("int64"),
    BOOLEAN("boolean"),
    STRING("string"),
    BYTES("bytes");

    private final String jsonName;

    ScalarType(String jsonName) {
        this.jsonName = jsonName;
    }

    /** The manifest wire name (e.g. {@code "boolean"}). */
    public String jsonName() {
        return jsonName;
    }

    /** Resolve a manifest wire name; returns {@code null} for unknown names. */
    public static ScalarType fromJsonName(String jsonName) {
        for (ScalarType type : values()) {
            if (type.jsonName.equals(jsonName)) {
                return type;
            }
        }
        return null;
    }
}
