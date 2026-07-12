package io.sqlitehost.model.manifest;

/**
 * The single scalar result field an inline function returns (inline
 * eligibility pins exactly one scalar result — docs/proposals/
 * inline-host-functions.md).
 */
public record InlineReturn(
        String propertyName,
        String sqlName,
        ScalarType scalarType) {
}
