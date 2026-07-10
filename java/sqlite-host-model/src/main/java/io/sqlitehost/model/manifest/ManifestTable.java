package io.sqlitehost.model.manifest;

import java.util.Collections;
import java.util.List;

/**
 * A shared table descriptor — the manifest {@code queueTable},
 * {@code inputsTable} and {@code varsTable} blocks share this
 * {name, columns} shape.
 */
public record ManifestTable(String name, List<String> columns) {

    public ManifestTable {
        columns = columns == null ? Collections.emptyList() : List.copyOf(columns);
    }
}
