package io.sqlitehost.jdbc.cli;

import io.sqlitehost.jdbc.PrepareOnlySqliteValidator;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.JsonReadException;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.validator.ValidationCodes;
import io.sqlitehost.validator.ValidationEngine;
import io.sqlitehost.validator.ValidationFinding;
import io.sqlitehost.validator.ValidationReport;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.List;

/**
 * Thin CLI over the full validator (library-first — this class only
 * parses arguments, reads files, runs the two engines, and prints
 * findings).
 *
 * <p>Usage: {@code sqlite-host-validator <manifest.json> <script.json>}.
 * Prints one finding per line; exits 1 when the script has errors,
 * 0 when publishable, 2 on usage or I/O failures.</p>
 *
 * <p><b>It runs all four layers.</b> It lives in {@code sqlite-host-jdbc}
 * rather than {@code sqlite-host-validator} for that reason: layer 3
 * (prepare-only SQLite) is here and this module already depends on the
 * validator, so the CLI has to sit on this side of the edge or the
 * module graph would cycle. While it ran the semantic lint alone it
 * exited 0 on {@code fixtures/payloads/invalid/unknown-column.json} —
 * a payload the conformance corpus calls invalid, whose only finding is
 * {@code sql-prepare-error} — and printed nothing at all. The gate is
 * the publication pipeline (docs/validation.md), so the artifact a
 * pipeline runs must be the whole gate.</p>
 *
 * <p>A script the strict reader refuses (docs/script-envelope.md — a
 * mistyped field, an unknown binding type, a non-integral {@code int32})
 * is a verdict about the payload, not about the tooling, so it prints an
 * {@code invalid-envelope} finding and exits 1 like any other error. Exit
 * 2 is reserved for the cases where no verdict was reached at all: bad
 * arguments, an unreadable file, a manifest that will not parse, or a
 * workspace layer 3 could not even set up.</p>
 */
public final class ValidatorCli {

    private ValidatorCli() {
    }

    public static void main(String[] args) {
        System.exit(run(args));
    }

    static int run(String[] args) {
        if (args.length != 2) {
            System.err.println("usage: sqlite-host-validator <manifest.json> <script.json>");
            return 2;
        }
        Manifest manifest;
        Script script;
        try {
            manifest = ManifestJsonReader.read(Files.readString(Path.of(args[0])));
        } catch (IOException e) {
            System.err.println("error reading manifest: " + e.getMessage());
            return 2;
        }
        try {
            script = ScriptJsonReader.read(Files.readString(Path.of(args[1])));
        } catch (JsonReadException e) {
            // The payload is well-formed JSON that violates the envelope
            // contract. That is an invalid-envelope verdict, and a CI gate
            // reading exit 2 would call it broken tooling instead.
            System.out.println(ValidationFinding
                    .error(ValidationCodes.INVALID_ENVELOPE, e.getMessage())
                    .render());
            return 1;
        } catch (IOException e) {
            System.err.println("error reading script: " + e.getMessage());
            return 2;
        }

        List<ValidationFinding> findings = new ArrayList<>();
        ValidationReport report = new ValidationEngine().validate(manifest, script);
        findings.addAll(report.findings());
        try {
            findings.addAll(new PrepareOnlySqliteValidator().validate(manifest, script));
        } catch (SQLException e) {
            // The in-memory workspace or the generated schema itself failed.
            // That is a host/manifest problem, not a verdict on the payload,
            // so it must not read as "publishable" or as "has errors".
            System.err.println("error preparing the validation workspace: " + e.getMessage());
            return 2;
        }
        for (ValidationFinding finding : findings) {
            System.out.println(finding.render());
        }
        // One definition of publishable for every caller: zero errors,
        // warnings don't block (docs/validation.md).
        return new ValidationReport(findings).isValid() ? 0 : 1;
    }
}
