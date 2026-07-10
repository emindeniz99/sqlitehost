package io.sqlitehost.jdbc;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.validator.Severity;
import io.sqlitehost.validator.ValidationEngine;
import io.sqlitehost.validator.ValidationFinding;
import io.sqlitehost.validator.ValidationReport;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.TestFactory;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The Java conformance matrix: every case in
 * fixtures/payloads/expectations.json whose {@code validators} include
 * {@code "java"} must behave exactly as specified when run through the
 * full engine — semantic lint plus prepare-only SQLite validation
 * (which is what makes the {@code sql-prepare-error} case pass).
 *
 * <p>Valid cases must produce zero errors and exactly the expected
 * warnings; invalid cases must include every expected code among their
 * errors (extra findings are allowed on invalid payloads).</p>
 */
class ConformanceTest {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    @TestFactory
    Stream<DynamicTest> expectationsMatrix() throws IOException {
        Path payloadsDir = Fixtures.fixturesDir().resolve("payloads");
        JsonNode expectations = MAPPER.readTree(
                Files.readString(payloadsDir.resolve("expectations.json")));

        Path manifestPath = payloadsDir
                .resolve(expectations.get("manifest").asText())
                .normalize();
        Manifest manifest = ManifestJsonReader.read(Files.readString(manifestPath));

        List<DynamicTest> tests = new ArrayList<>();
        for (JsonNode caseNode : expectations.get("cases")) {
            String payload = caseNode.get("payload").asText();
            tests.add(DynamicTest.dynamicTest(payload,
                    () -> runCase(manifest, payloadsDir, caseNode)));
        }
        return tests.stream();
    }

    private void runCase(Manifest manifest, Path payloadsDir, JsonNode caseNode)
            throws Exception {
        String payload = caseNode.get("payload").asText();
        boolean valid = caseNode.get("valid").asBoolean();

        Script script = ScriptJsonReader.read(
                Files.readString(payloadsDir.resolve(payload)));

        // Full Java engine: semantic lint + prepare-only SQLite checks.
        ValidationReport semantic = new ValidationEngine().validate(manifest, script);
        List<ValidationFinding> findings = new ArrayList<>(semantic.findings());
        findings.addAll(new PrepareOnlySqliteValidator().validate(manifest, script));

        List<String> errorCodes = codes(findings, Severity.ERROR);
        List<String> warningCodes = codes(findings, Severity.WARNING);

        if (valid) {
            assertEquals(List.of(), errorCodes,
                    payload + ": valid payloads must produce zero errors");
            assertEquals(expectedCodes(caseNode.get("warnings")),
                    warningCodes.stream().sorted().toList(),
                    payload + ": valid payloads must produce exactly the expected warnings");
        } else {
            assertTrue(errorCodes.size() > 0,
                    payload + ": invalid payloads must produce errors");
            for (String expected : expectedCodes(caseNode.get("errors"))) {
                assertTrue(errorCodes.contains(expected),
                        payload + ": expected error code '" + expected
                                + "' among " + errorCodes);
            }
        }
    }

    /** The codes this implementation must report (validators include "java"). */
    private static List<String> expectedCodes(JsonNode entries) {
        List<String> codes = new ArrayList<>();
        if (entries == null) {
            return codes;
        }
        for (JsonNode entry : entries) {
            for (JsonNode validator : entry.get("validators")) {
                if ("java".equals(validator.asText())) {
                    codes.add(entry.get("code").asText());
                    break;
                }
            }
        }
        return codes.stream().sorted().toList();
    }

    private static List<String> codes(List<ValidationFinding> findings, Severity severity) {
        return findings.stream()
                .filter(f -> f.severity() == severity)
                .map(ValidationFinding::code)
                .toList();
    }
}
