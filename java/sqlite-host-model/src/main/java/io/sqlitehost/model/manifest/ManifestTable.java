package io.sqlitehost.model.manifest;

import java.util.Collections;
import java.util.List;

/**
 * A shared table descriptor — the manifest {@code queueTable} and
 * {@code inputsTable} blocks share this {name, columns} shape.
 */
public record ManifestTable(String name, List<String> columns) {

    public ManifestTable {
        columns = columns == null ? Collections.emptyList() : List.copyOf(columns);
    }
}
