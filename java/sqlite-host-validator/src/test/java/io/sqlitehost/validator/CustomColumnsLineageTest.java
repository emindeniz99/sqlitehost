package io.sqlitehost.validator;

import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The lint keys on the manifest columns block, not on the default
 * column names: in a host whose call-id column is 'cid', lineage is
 * resolved through 'cid' comparisons and inserts — and a literal
 * 'call_id' identifier means nothing.
 */
class CustomColumnsLineageTest {

    private static Manifest manifest;

    @BeforeAll
    static void loadManifest() throws IOException {
        manifest = ManifestJsonReader.read(
                "{\"manifestVersion\":1,\"engine\":\"sqlite-host-v1\","
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
                + "\"scriptEnvelope\":{\"engine\":\"sqlite-host-v1\",\"bindingTypes\":"
                + "[\"null\",\"int32\",\"int64\",\"bool\",\"text\",\"blob\",\"float32\",\"float64\"]},"
                + "\"methods\":[{\"operationName\":\"GetValue\",\"methodName\":\"getValue\","
                + "\"handlerName\":\"GetValue\",\"apiLevel\":1,\"mutates\":true,"
                + "\"callTable\":\"call_get_value\",\"resultTable\":\"result_get_value\","
                + "\"queueTrigger\":\"trg_call_get_value_queue\","
                + "\"input\":{\"modelName\":\"GetValueInput\",\"fields\":["
                + "{\"propertyName\":\"key\",\"sqlName\":\"key\",\"column\":\"input_key\","
                + "\"scalarType\":\"string\",\"optional\":false}],\"listFields\":[]},"
                + "\"result\":{\"modelName\":\"GetValueResult\",\"fields\":["
                + "{\"propertyName\":\"value\",\"sqlName\":\"value\",\"column\":\"result_value\","
                + "\"scalarType\":\"int64\",\"optional\":false}],\"listFields\":[]},"
                + "\"inline\":null}]}");
    }

    private static ValidationReport validate(String scriptJson) throws IOException {
        Script script = ScriptJsonReader.read(scriptJson);
        return new ValidationEngine().validate(manifest, script);
    }

    private static List<String> errorCodes(ValidationReport report) {
        return report.errors().stream().map(ValidationFinding::code).toList();
    }

    /** Step 0 emits getValue 'read-1' through the cid column; step 1 reads with the given filter. */
    private static String emitThenRead(String readWhere) {
        return "{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\"],"
                + "\"steps\":["
                + "{\"id\":\"read\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (cid, input_key) VALUES (:c, 'k')\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"read-1\"}}}]},"
                + "{\"id\":\"use\",\"statements\":["
                + "{\"sql\":\"SELECT result_value FROM result_get_value WHERE " + readWhere + "\","
                + "\"bindings\":{}}]}]}";
    }

    @Test
    void cidComparisonResolvesLineage() throws IOException {
        // The read filters on the manifest call-id column: lineage
        // resolves against the step-0 emit — no findings.
        ValidationReport report = validate(emitThenRead("cid = 'read-1' AND state = 'ok'"));
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void cidComparisonWithUnknownIdIsFlagged() throws IOException {
        // Same shape, unknown id: proves the cid comparison is what
        // feeds the lineage check (it fires, so the read was collected).
        ValidationReport report = validate(emitThenRead("cid = 'zz'"));
        assertTrue(errorCodes(report).contains(ValidationCodes.RESULT_READ_UNKNOWN_CALL),
                errorCodes(report).toString());
    }

    @Test
    void defaultCallIdComparisonDoesNotFeedLineageInACidWorld() throws IOException {
        // 'call_id' is not this host's call-id column — the comparison
        // is not a lineage key, so no result-read finding fires (the
        // nonexistent column is prepare-only validation's business).
        ValidationReport report = validate(emitThenRead("call_id = 'zz'"));
        List<String> codes = errorCodes(report);
        assertFalse(codes.contains(ValidationCodes.RESULT_READ_UNKNOWN_CALL), codes.toString());
        assertFalse(codes.contains(ValidationCodes.RESULT_READ_NOT_AFTER_CALL), codes.toString());
    }

    @Test
    void emitSideAlsoResolvesThroughTheCidColumn() throws IOException {
        // A read colocated with its emit is only detectable when the
        // emit's cid cell was statically resolved — the finding proves
        // the insert side keys on the manifest column too.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\"],"
                + "\"steps\":[{\"id\":\"same-step\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (cid, input_key) VALUES (:c, 'k')\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"read-1\"}}},"
                + "{\"sql\":\"SELECT result_value FROM result_get_value WHERE cid = 'read-1'\","
                + "\"bindings\":{}}]}]}");
        assertTrue(errorCodes(report).contains(ValidationCodes.RESULT_READ_NOT_AFTER_CALL),
                errorCodes(report).toString());
    }
}
