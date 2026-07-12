package io.sqlitehost.model.manifest;

/**
 * One inline function argument — an input scalar field in declaration
 * order; optional fields are trailing (docs/proposals/
 * inline-host-functions.md).
 */
public record InlineArg(
        String propertyName,
        String sqlName,
        ScalarType scalarType,
        boolean optional) {
}
