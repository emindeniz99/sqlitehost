package io.sqlitehost.model.manifest;

import java.util.Collections;
import java.util.List;

/** An input or result object shape: scalar fields plus list fields. */
public record ObjectShape(
        String modelName,
        List<ScalarField> fields,
        List<ListField> listFields) {

    public ObjectShape {
        fields = fields == null ? Collections.emptyList() : List.copyOf(fields);
        listFields = listFields == null ? Collections.emptyList() : List.copyOf(listFields);
    }
}
