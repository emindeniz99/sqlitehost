package io.sqlitehost.model;

import io.sqlitehost.model.json.JsonReadException;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.model.manifest.MethodDescriptor;
import io.sqlitehost.model.manifest.ScalarType;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.file.Files;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The manifest model must mirror the committed manifest JSON exactly. */
class ManifestReaderTest {

    private static Manifest readSampleManifest() throws IOException {
        return ManifestJsonReader.read(Files.readString(
                Fixtures.fixturesDir().resolve("manifests/sample-host.manifest.json")));
    }

    @Test
    void readsTopLevelBlocks() throws IOException {
        Manifest manifest = readSampleManifest();
        assertEquals(1, manifest.manifestVersion());
        assertEquals("sqlite-host-v1", manifest.engine());
        assertEquals("Example.Game", manifest.library().namespace());
        assertEquals("GameHostMethods", manifest.library().interfaceName());
        assertEquals(1, manifest.library().apiLevel());
        assertEquals(3019003, manifest.library().minSqliteVersionNumber());
        assertEquals(List.of("typedNamedBindings", "splitResultTables", "scriptInputs",
                        "scriptVars"),
                manifest.library().features());
        assertEquals("call_", manifest.naming().callTablePrefix());
        assertEquals("__result_", manifest.naming().resultListTableInfix());
        assertEquals("pending_host_calls", manifest.queueTable().name());
        assertEquals(List.of("queue_id", "call_id", "method", "status"),
                manifest.queueTable().columns());
        assertEquals("script_inputs", manifest.inputsTable().name());
        assertEquals("script_vars", manifest.varsTable().name());
        assertEquals(
                List.of("name", "value_type", "int_value", "real_value", "text_value",
                        "blob_value"),
                manifest.varsTable().columns());
        assertEquals("sqlite-host-v1", manifest.scriptEnvelope().engine());
        assertEquals(
                List.of("null", "int32", "int64", "bool", "text", "blob", "float32", "float64"),
                manifest.scriptEnvelope().bindingTypes());
    }

    @Test
    void readsMethodsInDeclarationOrderWithResolvedNames() throws IOException {
        Manifest manifest = readSampleManifest();
        assertEquals(5, manifest.methods().size());
        assertEquals(List.of("getValue", "setValue", "getValues", "putBlob", "recordScore"),
                manifest.methods().stream().map(MethodDescriptor::methodName).toList());

        MethodDescriptor getValues = manifest.methodByName("getValues");
        assertEquals("GetValues", getValues.operationName());
        assertEquals("call_get_values", getValues.callTable());
        assertEquals("result_get_values", getValues.resultTable());
        assertEquals("trg_call_get_values_queue", getValues.queueTrigger());

        // Optional scalar + input list field.
        assertEquals(1, getValues.input().fields().size());
        assertEquals("input_default_value", getValues.input().fields().get(0).column());
        assertEquals(ScalarType.INT64, getValues.input().fields().get(0).scalarType());
        assertTrue(getValues.input().fields().get(0).optional());
        assertEquals(1, getValues.input().listFields().size());
        assertEquals("call_get_values__input_keys",
                getValues.input().listFields().get(0).childTable());
        assertEquals("KeyQueryItem", getValues.input().listFields().get(0).itemModelName());

        // Result list field with three typed item columns.
        var entries = getValues.result().listFields().get(0);
        assertEquals("result_get_values__result_entries", entries.childTable());
        assertEquals(3, entries.itemFields().size());
        assertEquals(ScalarType.BOOLEAN, entries.itemFields().get(2).scalarType());

        MethodDescriptor putBlob = manifest.methodByName("putBlob");
        assertEquals(ScalarType.BYTES, putBlob.input().fields().get(1).scalarType());
        assertTrue(putBlob.input().fields().get(2).optional());

        // Float scalars: required float64 score, optional float32 weight.
        MethodDescriptor recordScore = manifest.methodByName("recordScore");
        assertEquals(ScalarType.FLOAT64, recordScore.input().fields().get(1).scalarType());
        assertEquals(ScalarType.FLOAT32, recordScore.input().fields().get(2).scalarType());
        assertTrue(recordScore.input().fields().get(2).optional());
        assertEquals(ScalarType.FLOAT64, recordScore.result().fields().get(0).scalarType());
    }

    /** A minimal manifest carrying every required block (methods empty). */
    private static String minimalManifestJson() {
        return "{\"manifestVersion\":1,\"engine\":\"sqlite-host-v1\","
                + "\"library\":{\"namespace\":\"N\",\"interfaceName\":\"I\",\"apiLevel\":1,"
                + "\"minSqliteVersionNumber\":3019003,\"features\":[]},"
                + "\"naming\":{\"callTablePrefix\":\"call_\",\"resultTablePrefix\":\"result_\","
                + "\"inputColumnPrefix\":\"input_\",\"resultColumnPrefix\":\"result_\","
                + "\"inputListTableInfix\":\"__input_\",\"resultListTableInfix\":\"__result_\"},"
                + "\"queueTable\":{\"name\":\"q\",\"columns\":[]},"
                + "\"inputsTable\":{\"name\":\"i\",\"columns\":[]},"
                + "\"varsTable\":{\"name\":\"v\",\"columns\":[]},"
                + "\"scriptEnvelope\":{\"engine\":\"sqlite-host-v1\",\"bindingTypes\":[]},"
                + "\"methods\":[]}";
    }

    @Test
    void minimalManifestParsesAndCarriesTheNewFields() throws IOException {
        Manifest manifest = ManifestJsonReader.read(minimalManifestJson());
        assertEquals(3019003, manifest.library().minSqliteVersionNumber());
        assertEquals("v", manifest.varsTable().name());
        assertEquals(List.of(), manifest.varsTable().columns());
    }

    @Test
    void missingMinSqliteVersionNumberIsAReaderError() {
        String json = minimalManifestJson()
                .replace("\"minSqliteVersionNumber\":3019003,", "");
        JsonReadException e = assertThrows(JsonReadException.class,
                () -> ManifestJsonReader.read(json));
        assertTrue(e.getMessage().contains("minSqliteVersionNumber"), e.getMessage());
    }

    @Test
    void missingVarsTableIsAReaderError() {
        String json = minimalManifestJson()
                .replace("\"varsTable\":{\"name\":\"v\",\"columns\":[]},", "");
        JsonReadException e = assertThrows(JsonReadException.class,
                () -> ManifestJsonReader.read(json));
        assertTrue(e.getMessage().contains("varsTable"), e.getMessage());
    }

    @Test
    void unknownScalarTypeIsAReaderError() {
        String json = "{\"manifestVersion\":1,\"engine\":\"sqlite-host-v1\","
                + "\"library\":{\"namespace\":\"N\",\"interfaceName\":\"I\",\"apiLevel\":1,"
                + "\"minSqliteVersionNumber\":3019003,\"features\":[]},"
                + "\"naming\":{\"callTablePrefix\":\"call_\",\"resultTablePrefix\":\"result_\","
                + "\"inputColumnPrefix\":\"input_\",\"resultColumnPrefix\":\"result_\","
                + "\"inputListTableInfix\":\"__input_\",\"resultListTableInfix\":\"__result_\"},"
                + "\"queueTable\":{\"name\":\"q\",\"columns\":[]},"
                + "\"inputsTable\":{\"name\":\"i\",\"columns\":[]},"
                + "\"varsTable\":{\"name\":\"v\",\"columns\":[]},"
                + "\"scriptEnvelope\":{\"engine\":\"sqlite-host-v1\",\"bindingTypes\":[]},"
                + "\"methods\":[{\"operationName\":\"Op\",\"methodName\":\"op\",\"handlerName\":\"Op\","
                + "\"apiLevel\":1,\"callTable\":\"call_op\",\"resultTable\":\"result_op\","
                + "\"queueTrigger\":\"trg_call_op_queue\","
                + "\"input\":{\"modelName\":\"OpInput\",\"fields\":[{\"propertyName\":\"x\","
                + "\"sqlName\":\"x\",\"column\":\"input_x\",\"scalarType\":\"float\",\"optional\":false}],"
                + "\"listFields\":[]},"
                + "\"result\":{\"modelName\":\"OpResult\",\"fields\":[],\"listFields\":[]}}]}";
        JsonReadException e = assertThrows(JsonReadException.class,
                () -> ManifestJsonReader.read(json));
        assertTrue(e.getMessage().contains("unknown scalar type 'float'"), e.getMessage());
    }
}
