package io.sqlitehost.model.manifest;

import java.util.Collections;
import java.util.List;

/** A list-of-objects field descriptor with its resolved child table. */
public record ListField(
        String propertyName,
        String sqlName,
        String childTable,
        String itemModelName,
        List<ScalarField> itemFields) {

    public ListField {
        itemFields = itemFields == null ? Collections.emptyList() : List.copyOf(itemFields);
    }
}
