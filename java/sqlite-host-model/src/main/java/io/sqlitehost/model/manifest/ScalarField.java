package io.sqlitehost.model.manifest;

/**
 * A scalar input/result field descriptor. Mirrors the manifest JSON
 * exactly: all physical names are resolved — consumers never re-derive
 * naming (docs/manifest.md).
 */
public record ScalarField(
        String propertyName,
        String sqlName,
        String column,
        ScalarType scalarType,
        boolean optional) {
}
