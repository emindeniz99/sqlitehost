package io.sqlitehost.model;

import io.sqlitehost.model.ddl.DdlGenerator;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;

/**
 * Every SQL-visible column name in the generated DDL must flow from
 * the manifest columns block (docs/naming.md) — a host with fully
 * renamed shared columns produces the same shapes under the custom
 * names, with no default name leaking through.
 */
class DdlGeneratorCustomColumnsTest {

    /** A host that renames every shared column and the done literal. */
    private static String customColumnsManifestJson() {
        return "{\"manifestVersion\":1,\"engine\":\"sqlite-host-v1\","
                + "\"library\":{\"namespace\":\"N\",\"interfaceName\":\"I\",\"apiLevel\":1,"
                + "\"minSqliteVersionNumber\":3019003,\"features\":[]},"
                + "\"naming\":{\"callTablePrefix\":\"call_\",\"resultTablePrefix\":\"result_\","
                + "\"inputColumnPrefix\":\"input_\",\"resultColumnPrefix\":\"result_\","
                + "\"inputListTableInfix\":\"__input_\",\"resultListTableInfix\":\"__result_\","
                + "\"functionPrefix\":\"fn_\"},"
                + "\"columns\":{\"callId\":\"cid\",\"itemIndex\":\"idx\","
                + "\"status\":\"state\",\"doneValue\":\"ok\",\"queueId\":\"qid\","
                + "\"method\":\"op\",\"name\":\"var_name\",\"valueType\":\"vt\","
                + "\"intValue\":\"iv\",\"realValue\":\"rv\","
                + "\"textValue\":\"tv\",\"blobValue\":\"bv\","
                + "\"action\":\"cmd\",\"message\":\"note\"},"
                + "\"queueTable\":{\"name\":\"q\",\"columns\":[\"qid\",\"cid\",\"op\",\"state\"]},"
                + "\"inputsTable\":{\"name\":\"ins\",\"columns\":"
                + "[\"var_name\",\"vt\",\"iv\",\"rv\",\"tv\",\"bv\"]},"
                + "\"varsTable\":{\"name\":\"vars\",\"columns\":"
                + "[\"var_name\",\"vt\",\"iv\",\"rv\",\"tv\",\"bv\"]},"
                + "\"controlTable\":{\"name\":\"ctl\",\"columns\":[\"cmd\",\"note\"]},"
                + "\"scriptEnvelope\":{\"engine\":\"sqlite-host-v1\",\"bindingTypes\":[]},"
                + "\"methods\":[{\"operationName\":\"GetValues\",\"methodName\":\"getValues\","
                + "\"handlerName\":\"GetValues\",\"apiLevel\":1,\"mutates\":true,"
                + "\"callTable\":\"call_get_values\",\"resultTable\":\"result_get_values\","
                + "\"queueTrigger\":\"trg_call_get_values_queue\","
                + "\"input\":{\"modelName\":\"GetValuesInput\",\"fields\":["
                + "{\"propertyName\":\"defaultValue\",\"sqlName\":\"default_value\","
                + "\"column\":\"input_default_value\",\"scalarType\":\"int64\",\"optional\":true}],"
                + "\"listFields\":[{\"propertyName\":\"keys\",\"sqlName\":\"keys\","
                + "\"childTable\":\"call_get_values__input_keys\",\"itemModelName\":\"KeyItem\","
                + "\"itemFields\":[{\"propertyName\":\"key\",\"sqlName\":\"key\","
                + "\"column\":\"input_key\",\"scalarType\":\"string\",\"optional\":false}]}]},"
                + "\"result\":{\"modelName\":\"GetValuesResult\",\"fields\":["
                + "{\"propertyName\":\"total\",\"sqlName\":\"total\","
                + "\"column\":\"result_total\",\"scalarType\":\"int64\",\"optional\":false}],"
                + "\"listFields\":[]},\"inline\":null}]}";
    }

    @Test
    void allTableAndTriggerEmissionUsesTheManifestColumnNames() throws IOException {
        Manifest manifest = ManifestJsonReader.read(customColumnsManifestJson());
        List<String> statements = DdlGenerator.generateSchemaStatements(manifest);
        assertEquals(8, statements.size());

        assertEquals(String.join("\n",
                "CREATE TABLE q (",
                "    qid INTEGER PRIMARY KEY AUTOINCREMENT,",
                "    cid TEXT NOT NULL UNIQUE,",
                "    op TEXT NOT NULL,",
                "    state TEXT NOT NULL DEFAULT 'pending'",
                ");"), statements.get(0));

        assertEquals(String.join("\n",
                "CREATE TABLE ins (",
                "    var_name TEXT NOT NULL PRIMARY KEY,",
                "    vt TEXT NOT NULL,",
                "    iv INTEGER,",
                "    rv REAL,",
                "    tv TEXT,",
                "    bv BLOB",
                ");"), statements.get(1));

        assertEquals(String.join("\n",
                "CREATE TABLE vars (",
                "    var_name TEXT NOT NULL PRIMARY KEY,",
                "    vt TEXT NOT NULL,",
                "    iv INTEGER,",
                "    rv REAL,",
                "    tv TEXT,",
                "    bv BLOB",
                ");"), statements.get(2));

        assertEquals(String.join("\n",
                "CREATE TABLE ctl (",
                "    cmd TEXT NOT NULL,",
                "    note TEXT",
                ");"), statements.get(3));

        assertEquals(String.join("\n",
                "CREATE TABLE call_get_values (",
                "    cid TEXT NOT NULL PRIMARY KEY,",
                "    input_default_value INTEGER",
                ");"), statements.get(4));

        assertEquals(String.join("\n",
                "CREATE TABLE call_get_values__input_keys (",
                "    cid TEXT NOT NULL,",
                "    idx INTEGER NOT NULL,",
                "    input_key TEXT NOT NULL,",
                "    PRIMARY KEY (cid, idx)",
                ");"), statements.get(5));

        assertEquals(String.join("\n",
                "CREATE TABLE result_get_values (",
                "    cid TEXT NOT NULL PRIMARY KEY,",
                "    state TEXT NOT NULL DEFAULT 'ok',",
                "    result_total INTEGER NOT NULL",
                ");"), statements.get(6));

        assertEquals(String.join("\n",
                "CREATE TRIGGER trg_call_get_values_queue",
                "AFTER INSERT ON call_get_values",
                "BEGIN",
                "    INSERT INTO q (cid, op)",
                "    VALUES (NEW.cid, 'getValues');",
                "END;"), statements.get(7));
    }
}
