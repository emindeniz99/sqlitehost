package io.sqlitehost.validator;

import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Inline function lint (docs/validation.md — feature inlineFunctions):
 * unknown-function keys on the manifest's functionPrefix, arity is
 * checked statically against minArgs..maxArgs, and an invoked inline
 * function both requires the feature declaration and exempts its
 * method from unused-required-method. The full fixture matrix runs in
 * sqlite-host-jdbc; these tests pin what it cannot express (custom
 * prefixes, builtin calls, case-insensitivity).
 */
class InlineFunctionLintTest {

    private static Manifest manifest;

    @BeforeAll
    static void loadManifest() throws IOException {
        Path dir = Paths.get("").toAbsolutePath();
        while (dir != null && !Files.isRegularFile(
                dir.resolve("fixtures/manifests/sample-host.manifest.json"))) {
            dir = dir.getParent();
        }
        if (dir == null) {
            throw new IllegalStateException("fixtures directory not found");
        }
        manifest = ManifestJsonReader.read(Files.readString(
                dir.resolve("fixtures/manifests/sample-host.manifest.json")));
    }

    private static ValidationReport validate(Manifest manifest, String scriptJson)
            throws IOException {
        Script script = ScriptJsonReader.read(scriptJson);
        return new ValidationEngine().validate(manifest, script);
    }

    private static List<String> errorCodes(ValidationReport report) {
        return report.errors().stream().map(ValidationFinding::code).toList();
    }

    /** A script whose single statement runs {@code sql} with no bindings. */
    private static String script(String features, String methods, String sql) {
        return "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                + "\"requiredFeatures\":[" + features + "],"
                + "\"requiredMethods\":[" + methods + "],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\"" + sql
                + "\",\"bindings\":{}}]}]}";
    }

    @Test
    void declaredInlineCallIsAccepted() throws IOException {
        ValidationReport report = validate(manifest, script(
                "\"inlineFunctions\"", "\"getValue\"",
                "SELECT fn_get_value('k')"));
        assertTrue(report.isValid(), report.findings().toString());
        assertEquals(List.of(), report.warnings().stream()
                .map(ValidationFinding::code).toList());
    }

    @Test
    void inlineCallWithoutTheFeatureIsUndeclaredFeatureUse() throws IOException {
        ValidationReport report = validate(manifest, script(
                "", "\"getValue\"", "SELECT fn_get_value('k')"));
        assertTrue(errorCodes(report).contains(ValidationCodes.UNDECLARED_FEATURE_USE),
                errorCodes(report).toString());
    }

    @Test
    void prefixMatchingUnknownIdentifierIsUnknownFunction() throws IOException {
        ValidationReport report = validate(manifest, script(
                "\"inlineFunctions\"", "", "SELECT fn_get_price('k')"));
        assertTrue(errorCodes(report).contains(ValidationCodes.UNKNOWN_FUNCTION),
                errorCodes(report).toString());
    }

    @Test
    void builtinFunctionsAreUntouched() throws IOException {
        // max/abs/coalesce carry no fn_ prefix — SQLite's business.
        ValidationReport report = validate(manifest, script(
                "", "", "SELECT max(1, 2), abs(-1), coalesce(NULL, 0)"));
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void tooManyArgumentsIsAnArityMismatch() throws IOException {
        ValidationReport report = validate(manifest, script(
                "\"inlineFunctions\"", "\"getValue\"",
                "SELECT fn_get_value('k', 'extra')"));
        assertTrue(errorCodes(report).contains(ValidationCodes.FUNCTION_ARITY_MISMATCH),
                errorCodes(report).toString());
    }

    @Test
    void tooFewArgumentsIsAnArityMismatch() throws IOException {
        ValidationReport report = validate(manifest, script(
                "\"inlineFunctions\"", "\"getValue\"", "SELECT fn_get_value()"));
        assertTrue(errorCodes(report).contains(ValidationCodes.FUNCTION_ARITY_MISMATCH),
                errorCodes(report).toString());
    }

    @Test
    void inlineInvocationExemptsUnusedRequiredMethod() throws IOException {
        // getValue's call table is never written, but its inline
        // function is invoked — no unused-required-method warning.
        ValidationReport report = validate(manifest, script(
                "\"inlineFunctions\"", "\"getValue\"", "SELECT fn_get_value('k')"));
        assertEquals(List.of(), report.warnings().stream()
                .map(ValidationFinding::code).toList());
    }

    @Test
    void unusedRequiredMethodStillWarnsWithoutAnInlineInvocation() throws IOException {
        ValidationReport report = validate(manifest, script(
                "\"inlineFunctions\"", "\"getValue\"", "SELECT 1"));
        assertTrue(report.warnings().stream().map(ValidationFinding::code).toList()
                .contains(ValidationCodes.UNUSED_REQUIRED_METHOD));
    }

    @Test
    void functionNamesMatchCaseInsensitively() throws IOException {
        // SQL identifiers are case-insensitive: FN_GET_VALUE is the
        // manifest's fn_get_value, not an unknown function.
        ValidationReport report = validate(manifest, script(
                "\"inlineFunctions\"", "\"getValue\"", "SELECT FN_GET_VALUE('k')"));
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void customFunctionPrefixDrivesTheMatching() throws IOException {
        // A host with functionPrefix 'udf_': 'fn_*' identifiers are no
        // longer special, and unknown 'udf_*' identifiers are flagged.
        Manifest custom = ManifestJsonReader.read(
                "{\"manifestVersion\":1,\"engine\":\"sqlite-host-v1\","
                + "\"library\":{\"namespace\":\"N\",\"interfaceName\":\"I\",\"apiLevel\":1,"
                + "\"minSqliteVersionNumber\":3019003,\"features\":[\"inlineFunctions\"]},"
                + "\"naming\":{\"callTablePrefix\":\"call_\",\"resultTablePrefix\":\"result_\","
                + "\"inputColumnPrefix\":\"input_\",\"resultColumnPrefix\":\"result_\","
                + "\"inputListTableInfix\":\"__input_\",\"resultListTableInfix\":\"__result_\","
                + "\"functionPrefix\":\"udf_\"},"
                + "\"columns\":{\"callId\":\"call_id\",\"itemIndex\":\"item_index\","
                + "\"status\":\"status\",\"doneValue\":\"done\",\"queueId\":\"queue_id\","
                + "\"method\":\"method\",\"name\":\"name\",\"valueType\":\"value_type\","
                + "\"intValue\":\"int_value\",\"realValue\":\"real_value\","
                + "\"textValue\":\"text_value\",\"blobValue\":\"blob_value\","
                + "\"action\":\"action\",\"message\":\"message\"},"
                + "\"queueTable\":{\"name\":\"q\",\"columns\":[]},"
                + "\"inputsTable\":{\"name\":\"i\",\"columns\":[]},"
                + "\"varsTable\":{\"name\":\"v\",\"columns\":[]},"
                + "\"controlTable\":{\"name\":\"c\",\"columns\":[]},"
                + "\"scriptEnvelope\":{\"engine\":\"sqlite-host-v1\",\"bindingTypes\":[]},"
                + "\"methods\":[{\"operationName\":\"GetValue\",\"methodName\":\"getValue\","
                + "\"handlerName\":\"GetValue\",\"apiLevel\":1,\"mutates\":false,"
                + "\"callTable\":\"call_get_value\",\"resultTable\":\"result_get_value\","
                + "\"queueTrigger\":\"trg_call_get_value_queue\","
                + "\"input\":{\"modelName\":\"GetValueInput\",\"fields\":["
                + "{\"propertyName\":\"key\",\"sqlName\":\"key\",\"column\":\"input_key\","
                + "\"scalarType\":\"string\",\"optional\":false}],\"listFields\":[]},"
                + "\"result\":{\"modelName\":\"GetValueResult\",\"fields\":["
                + "{\"propertyName\":\"value\",\"sqlName\":\"value\",\"column\":\"result_value\","
                + "\"scalarType\":\"int64\",\"optional\":false}],\"listFields\":[]},"
                + "\"inline\":{\"functionName\":\"udf_get_value\",\"minArgs\":1,\"maxArgs\":1,"
                + "\"args\":[{\"propertyName\":\"key\",\"sqlName\":\"key\","
                + "\"scalarType\":\"string\",\"optional\":false}],"
                + "\"returns\":{\"propertyName\":\"value\",\"sqlName\":\"value\","
                + "\"scalarType\":\"int64\"}}}]}");

        ValidationReport known = validate(custom, script(
                "\"inlineFunctions\"", "\"getValue\"", "SELECT udf_get_value('k')"));
        assertTrue(known.isValid(), known.findings().toString());

        ValidationReport unknownUdf = validate(custom, script(
                "\"inlineFunctions\"", "", "SELECT udf_bogus('k')"));
        assertTrue(errorCodes(unknownUdf).contains(ValidationCodes.UNKNOWN_FUNCTION),
                errorCodes(unknownUdf).toString());

        // 'fn_get_value' does not carry this host's prefix — no
        // unknown-function; prepare-only validation is what fails it.
        ValidationReport fnIsNotSpecial = validate(custom, script(
                "\"inlineFunctions\"", "", "SELECT fn_get_value('k')"));
        assertFalse(errorCodes(fnIsNotSpecial).contains(ValidationCodes.UNKNOWN_FUNCTION),
                errorCodes(fnIsNotSpecial).toString());
    }
}
