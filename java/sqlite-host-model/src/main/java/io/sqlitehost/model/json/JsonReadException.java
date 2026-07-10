package io.sqlitehost.model.json;

import java.io.IOException;

/**
 * A JSON document violates the SqliteHost contract it is being read
 * as (e.g. an unknown envelope binding type, a wrong field type).
 */
public class JsonReadException extends IOException {

    public JsonReadException(String message) {
        super(message);
    }
}
