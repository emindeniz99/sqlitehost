package io.sqlitehost.validator.cli;

import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.validator.ValidationEngine;
import io.sqlitehost.validator.ValidationFinding;
import io.sqlitehost.validator.ValidationReport;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Thin CLI over {@link ValidationEngine} (library-first — this class
 * only parses arguments, reads files, and prints findings).
 *
 * <p>Usage: {@code sqlite-host-validator <manifest.json> <script.json>}.
 * Prints one finding per line; exits 1 when the script has errors,
 * 0 when publishable, 2 on usage or read failures.</p>
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
        } catch (IOException e) {
            System.err.println("error reading script: " + e.getMessage());
            return 2;
        }
        ValidationReport report = new ValidationEngine().validate(manifest, script);
        for (ValidationFinding finding : report.findings()) {
            System.out.println(finding.render());
        }
        return report.isValid() ? 0 : 1;
    }
}
