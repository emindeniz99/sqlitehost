package io.sqlitehost.jdbc;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.JsonReadException;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.validator.Severity;
import io.sqlitehost.validator.ValidationCodes;
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
 * <p><strong>Exact match, both verdicts.</strong> The reported error
 * codes and the reported warning codes must each equal the expected
 * list — no extra findings, no missing ones. The comparison is over
 * sorted lists rather than sets, so multiplicity counts: a code
 * reported twice is two findings and must be expected twice. Every
 * invalid fixture is single-fault by construction (enforced by
 * scripts/check-fixture-corpus.mjs), so in practice each one expects
 * exactly one error.</p>
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
        String json = Files.readString(payloadsDir.resolve(payload));

        Script script;
        List<ValidationFinding> findings = new ArrayList<>();
        try {
            script = ScriptJsonReader.read(json);
        } catch (JsonReadException e) {
            // Some envelope violations are only visible in the JSON text
            // (a non-integral int32, an explicit null for an optional
            // field), so the strict reader is the only thing that can see
            // them and it reports by throwing. Mirror ValidatorCli: that
            // is an invalid-envelope verdict about the payload, not a
            // failure of the harness.
            findings.add(ValidationFinding.error(
                    ValidationCodes.INVALID_ENVELOPE, e.getMessage()));
            assertTrue(!valid, payload + ": the strict reader rejected a valid fixture: "
                    + e.getMessage());
            assertExactCodes(payload, caseNode, findings);
            return;
        }

        // Full Java engine: semantic lint + prepare-only SQLite checks.
        ValidationReport semantic = new ValidationEngine().validate(manifest, script);
        findings.addAll(semantic.findings());
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
            assertExactCodes(payload, caseNode, findings);
        }
    }

    /**
     * Invalid payloads: the reported errors and warnings must each equal
     * the expected list exactly. Containment used to be enough here, and
     * every validator divergence found in two audit rounds walked through
     * that gap — an extra finding is either a fixture that is not
     * single-fault or a validator that disagrees with its peer, and both
     * are things this matrix exists to catch.
     */
    private static void assertExactCodes(
            String payload, JsonNode caseNode, List<ValidationFinding> findings) {
        List<String> errorCodes = codes(findings, Severity.ERROR);
        List<String> warningCodes = codes(findings, Severity.WARNING);
        assertTrue(errorCodes.size() > 0,
                payload + ": invalid payloads must produce errors");
        assertEquals(expectedCodes(caseNode.get("errors")),
                errorCodes.stream().sorted().toList(),
                payload + ": invalid payloads must produce exactly the expected errors");
        assertEquals(expectedCodes(caseNode.get("warnings")),
                warningCodes.stream().sorted().toList(),
                payload + ": invalid payloads must produce exactly the expected warnings");
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
