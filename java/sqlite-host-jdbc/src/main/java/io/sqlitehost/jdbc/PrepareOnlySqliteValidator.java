package io.sqlitehost.jdbc;

import io.sqlitehost.model.ddl.DdlGenerator;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.envelope.Step;
import io.sqlitehost.model.manifest.InlineFunction;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.model.manifest.MethodDescriptor;
import io.sqlitehost.validator.ValidationCodes;
import io.sqlitehost.validator.ValidationFinding;
import org.sqlite.Function;

import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.List;

/**
 * Prepare-only SQLite validation (docs/validation.md layer 3): opens
 * an in-memory SQLite database, creates the generated schema from the
 * manifest, <b>prepares</b> every script statement — compile only,
 * catching grammar errors, missing tables/columns, and unsupported
 * functions — and finalizes without stepping. Each failed prepare is
 * reported as a {@code sql-prepare-error} finding with the statement's
 * step id and index.
 */
public final class PrepareOnlySqliteValidator {

    /**
     * Prepare every statement of {@code script} against the schema
     * generated from {@code manifest}.
     *
     * @return one {@code sql-prepare-error} finding per statement that
     *     failed to prepare (empty when everything compiles)
     * @throws SQLException when the workspace itself cannot be set up
     *     (opening the in-memory database or executing the generated
     *     schema DDL fails) — a host/manifest problem, not a script
     *     finding
     */
    public List<ValidationFinding> validate(Manifest manifest, Script script)
            throws SQLException {
        List<ValidationFinding> findings = new ArrayList<>();
        try (Connection connection = DriverManager.getConnection("jdbc:sqlite::memory:")) {
            createSchema(connection, manifest);
            registerInlineFunctionStubs(connection, manifest);
            for (Step step : script.steps()) {
                List<io.sqlitehost.model.envelope.Statement> statements = step.statements();
                for (int i = 0; i < statements.size(); i++) {
                    String sql = statements.get(i).sql();
                    if (sql == null || sql.isBlank()) {
                        continue; // structurally invalid — the semantic lint reports it
                    }
                    try (PreparedStatement prepared = connection.prepareStatement(sql)) {
                        // Prepared successfully; close() finalizes without stepping.
                    } catch (SQLException e) {
                        findings.add(ValidationFinding.error(
                                ValidationCodes.SQL_PREPARE_ERROR,
                                step.id(), i,
                                "statement failed to prepare: " + e.getMessage()));
                    }
                }
            }
        }
        return findings;
    }

    private static void createSchema(Connection connection, Manifest manifest)
            throws SQLException {
        try (java.sql.Statement statement = connection.createStatement()) {
            for (String ddl : DdlGenerator.generateSchemaStatements(manifest)) {
                statement.execute(ddl);
            }
        }
    }

    /**
     * Register a NULL-returning stub for every manifest inline function
     * at every arity in minArgs..maxArgs, so the function form compiles
     * during prepare (docs/proposals/inline-host-functions.md — the
     * Java prepare-only plan). The stubs are never stepped; identifiers
     * outside the manifest still fail prepare with "no such function".
     */
    private static void registerInlineFunctionStubs(Connection connection, Manifest manifest)
            throws SQLException {
        for (MethodDescriptor method : manifest.methods()) {
            InlineFunction inline = method.inline();
            if (inline == null) {
                continue;
            }
            for (int arity = inline.minArgs(); arity <= inline.maxArgs(); arity++) {
                Function.create(connection, inline.functionName(), new Function() {
                    @Override
                    protected void xFunc() {
                        // No result set — SQL NULL. Prepare-only: never invoked.
                    }
                }, arity, 0);
            }
        }
    }
}
