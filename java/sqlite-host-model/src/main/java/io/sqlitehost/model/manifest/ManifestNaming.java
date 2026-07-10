package io.sqlitehost.model.manifest;

/** The six host-level naming conventions (docs/naming.md). */
public record ManifestNaming(
        String callTablePrefix,
        String resultTablePrefix,
        String inputColumnPrefix,
        String resultColumnPrefix,
        String inputListTableInfix,
        String resultListTableInfix) {
}
