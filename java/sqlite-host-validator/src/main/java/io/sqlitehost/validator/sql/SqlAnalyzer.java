package io.sqlitehost.validator.sql;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Statement-shape analysis over the token stream: INSERT parsing,
 * {@code identifier(...)} function-call extraction, and
 * {@code <callIdColumn> = <atom>} comparison extraction (the call-id
 * column name comes from the manifest columns block). Best-effort
 * static analysis for lint purposes (docs/validation.md) — not a SQL
 * parser.
 */
public final class SqlAnalyzer {

    private static final Set<String> SELECT_ITEM_TERMINATORS =
            Set.of("from", "where", "group", "order", "limit", "union", "except", "intersect");

    private SqlAnalyzer() {
    }

    /**
     * Parse the statement as an INSERT, or return {@code null} when it
     * is not one. Handles {@code INSERT [OR …] INTO <table>} with an
     * optional column list, followed by {@code VALUES (…) [, (…)]…},
     * {@code SELECT …} (first top-level items map to the column list),
     * or {@code DEFAULT VALUES}.
     */
    public static InsertStatement parseInsert(List<SqlToken> tokens) {
        int i = indexOfIdent(tokens, "insert");
        if (i < 0) {
            return null;
        }
        // Skip conflict clause idents (OR REPLACE / OR IGNORE / …) up to INTO.
        int into = -1;
        for (int j = i + 1; j < tokens.size() && j <= i + 3; j++) {
            if (tokens.get(j).isIdent("into")) {
                into = j;
                break;
            }
            if (tokens.get(j).kind() != SqlToken.Kind.IDENT) {
                return null;
            }
        }
        if (into < 0 || into + 1 >= tokens.size()) {
            return null;
        }
        int pos = into + 1;
        if (tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
            return null;
        }
        String table = tokens.get(pos).text();
        pos++;
        // Schema-qualified name: keep the last component.
        while (pos + 1 < tokens.size()
                && tokens.get(pos).isPunct(".")
                && tokens.get(pos + 1).kind() == SqlToken.Kind.IDENT) {
            table = tokens.get(pos + 1).text();
            pos += 2;
        }

        // Optional `AS <alias>` between the table name and the column
        // list (valid SQLite >= 3.24.0, e.g. INSERT INTO t AS c (...) …).
        // Skip it so the explicit column list is still recognized. Only
        // the `AS <ident>` form is handled — a bare alias is a syntax
        // error for INSERT targets, and VALUES/SELECT/DEFAULT are idents
        // that must not be swallowed.
        if (pos < tokens.size() && tokens.get(pos).isIdent("as")) {
            pos++;
            if (pos < tokens.size() && tokens.get(pos).kind() == SqlToken.Kind.IDENT) {
                pos++;
            }
        }

        List<String> columns = null;
        if (pos < tokens.size() && tokens.get(pos).isPunct("(")) {
            columns = new ArrayList<>();
            pos++;
            while (pos < tokens.size() && !tokens.get(pos).isPunct(")")) {
                if (tokens.get(pos).kind() == SqlToken.Kind.IDENT) {
                    columns.add(tokens.get(pos).text());
                }
                pos++;
            }
            if (pos < tokens.size()) {
                pos++; // consume ')'
            }
        }

        if (pos < tokens.size() && tokens.get(pos).isIdent("values")) {
            return new InsertStatement(table, columns, parseValuesRows(tokens, pos + 1), null);
        }
        if (pos < tokens.size() && tokens.get(pos).isIdent("select")) {
            return new InsertStatement(table, columns, null, parseSelectItems(tokens, pos + 1));
        }
        if (pos + 1 < tokens.size()
                && tokens.get(pos).isIdent("default")
                && tokens.get(pos + 1).isIdent("values")) {
            return new InsertStatement(table, columns, null, null);
        }
        return new InsertStatement(table, columns, null, null);
    }

    /** Parse {@code (expr, …) [, (expr, …)]…} rows after VALUES. */
    private static List<List<ValueExpr>> parseValuesRows(List<SqlToken> tokens, int start) {
        List<List<ValueExpr>> rows = new ArrayList<>();
        int pos = start;
        while (pos < tokens.size() && tokens.get(pos).isPunct("(")) {
            List<ValueExpr> row = new ArrayList<>();
            List<SqlToken> current = new ArrayList<>();
            int depth = 1;
            pos++;
            while (pos < tokens.size() && depth > 0) {
                SqlToken token = tokens.get(pos);
                if (token.isPunct("(")) {
                    depth++;
                } else if (token.isPunct(")")) {
                    depth--;
                    if (depth == 0) {
                        pos++;
                        break;
                    }
                } else if (token.isPunct(",") && depth == 1) {
                    row.add(ValueExpr.classify(current));
                    current = new ArrayList<>();
                    pos++;
                    continue;
                }
                current.add(token);
                pos++;
            }
            if (!current.isEmpty()) {
                row.add(ValueExpr.classify(current));
            }
            rows.add(row);
            // Another row?
            if (pos < tokens.size() && tokens.get(pos).isPunct(",")) {
                pos++;
                continue;
            }
            break;
        }
        return rows;
    }

    /** Parse top-level SELECT items until FROM/WHERE/… at depth 0. */
    private static List<ValueExpr> parseSelectItems(List<SqlToken> tokens, int start) {
        List<ValueExpr> items = new ArrayList<>();
        List<SqlToken> current = new ArrayList<>();
        int depth = 0;
        for (int pos = start; pos < tokens.size(); pos++) {
            SqlToken token = tokens.get(pos);
            if (token.isPunct("(")) {
                depth++;
            } else if (token.isPunct(")")) {
                depth--;
            } else if (depth == 0 && token.kind() == SqlToken.Kind.IDENT
                    && SELECT_ITEM_TERMINATORS.contains(token.text().toLowerCase())) {
                break;
            } else if (depth == 0 && token.isPunct(",")) {
                items.add(ValueExpr.classify(current));
                current = new ArrayList<>();
                continue;
            }
            current.add(token);
        }
        if (!current.isEmpty()) {
            items.add(ValueExpr.classify(current));
        }
        return items;
    }

    /**
     * Extract every {@code identifier(...)} function call: an
     * {@link SqlToken.Kind#IDENT} immediately followed by {@code '('},
     * with the argument count taken by a top-level comma scan to the
     * matching {@code ')'}. String literals and comments never confuse
     * the scan — the tokenizer already collapsed them. Calls nested in
     * another call's arguments are extracted as their own entries.
     */
    public static List<FunctionCall> functionCalls(List<SqlToken> tokens) {
        List<FunctionCall> calls = new ArrayList<>();
        for (int i = 0; i + 1 < tokens.size(); i++) {
            if (tokens.get(i).kind() == SqlToken.Kind.IDENT
                    && tokens.get(i + 1).isPunct("(")) {
                calls.add(new FunctionCall(tokens.get(i).text(),
                        countArgs(tokens, i + 2), hasNowArg(tokens, i + 2)));
            }
        }
        return calls;
    }

    /** Count top-level arguments from just after '(' to the matching ')'. */
    private static int countArgs(List<SqlToken> tokens, int start) {
        int depth = 1;
        int commas = 0;
        boolean sawArgToken = false;
        for (int pos = start; pos < tokens.size(); pos++) {
            SqlToken token = tokens.get(pos);
            if (token.isPunct("(")) {
                depth++;
            } else if (token.isPunct(")")) {
                depth--;
                if (depth == 0) {
                    return sawArgToken ? commas + 1 : 0;
                }
            } else if (token.isPunct(",") && depth == 1) {
                commas++;
            }
            sawArgToken = true;
        }
        return FunctionCall.UNKNOWN_ARGS;
    }

    /**
     * Whether some top-level argument from just after '(' to the matching
     * ')' is exactly the string literal {@code 'now'} (case-insensitive).
     * Only a bare literal counts: {@code datetime('now')} reads the clock,
     * {@code datetime(:when)} does not, and a literal nested inside a
     * larger expression is not the argument itself.
     */
    private static boolean hasNowArg(List<SqlToken> tokens, int start) {
        int depth = 1;
        int argTokens = 0;
        boolean argIsNow = false;
        for (int pos = start; pos < tokens.size(); pos++) {
            SqlToken token = tokens.get(pos);
            if (token.isPunct(")")) {
                depth--;
                if (depth == 0) {
                    return argTokens == 1 && argIsNow;
                }
            } else if (token.isPunct("(")) {
                depth++;
            } else if (token.isPunct(",") && depth == 1) {
                if (argTokens == 1 && argIsNow) {
                    return true;
                }
                argTokens = 0;
                argIsNow = false;
                continue;
            }
            if (argTokens == 0) {
                argIsNow = isNowLiteral(token);
            }
            argTokens++;
        }
        return false;
    }

    private static boolean isNowLiteral(SqlToken token) {
        return token.kind() == SqlToken.Kind.STRING && "now".equalsIgnoreCase(token.text());
    }

    /**
     * Extract {@code <callIdColumn> = <atom>} (and
     * {@code <atom> = <callIdColumn>}) comparisons where the atom is a
     * single string literal or named parameter. {@code callIdColumn} is
     * the manifest's call-id column name. Concatenations and other
     * computed expressions are not atoms — they are skipped by static
     * call-id resolution.
     */
    public static List<ValueExpr> callIdComparisons(List<SqlToken> tokens, String callIdColumn) {
        List<ValueExpr> comparisons = new ArrayList<>();
        for (int i = 0; i < tokens.size(); i++) {
            if (!tokens.get(i).isIdent(callIdColumn)) {
                continue;
            }
            // forward form: <callIdColumn> = <atom>
            if (i + 2 < tokens.size() && tokens.get(i + 1).isPunct("=")) {
                SqlToken value = tokens.get(i + 2);
                if (isAtom(value) && !continuesExpression(tokens, i + 3)) {
                    comparisons.add(atom(value));
                }
            }
            // reverse form: <atom> = <callIdColumn>
            if (i >= 2 && tokens.get(i - 1).isPunct("=")) {
                SqlToken value = tokens.get(i - 2);
                if (isAtom(value) && (i - 3 < 0 || !tokens.get(i - 3).isPunct("||"))) {
                    comparisons.add(atom(value));
                }
            }
        }
        return comparisons;
    }

    private static boolean isAtom(SqlToken token) {
        return token.kind() == SqlToken.Kind.STRING || token.kind() == SqlToken.Kind.PARAM;
    }

    private static boolean continuesExpression(List<SqlToken> tokens, int index) {
        return index < tokens.size()
                && (tokens.get(index).isPunct("||") || tokens.get(index).isPunct("."));
    }

    private static ValueExpr atom(SqlToken token) {
        return token.kind() == SqlToken.Kind.STRING
                ? new ValueExpr(ValueExpr.Kind.STRING, token.text())
                : new ValueExpr(ValueExpr.Kind.PARAM, token.text());
    }

    /**
     * The statement's first meaningful token, lowercased, when that token is
     * an identifier — the anchor of the forbidden-statement lint
     * (docs/validation.md). The tokenizer has already dropped whitespace and
     * both comment forms, so token 0 <em>is</em> the first meaningful token;
     * nothing extra is needed to be comment-aware. Returns {@code null} when
     * the statement is empty or starts with a non-identifier (a string
     * literal, punctuation), so a leading {@code 'PRAGMA'} literal is never
     * mistaken for the PRAGMA statement.
     */
    public static String leadingKeyword(List<SqlToken> tokens) {
        if (tokens.isEmpty() || tokens.get(0).kind() != SqlToken.Kind.IDENT) {
            return null;
        }
        return tokens.get(0).text().toLowerCase(Locale.ROOT);
    }

    /**
     * Whether the token stream holds more than one SQL statement: a top-level
     * (paren depth 0) {@code ';'} punctuation token followed by at least one
     * further token — the anchor of the multiple-statements lint
     * (docs/validation.md). A trailing {@code ';'} that merely terminates a
     * single statement (nothing follows it) is legal and not flagged. Comments
     * and string literals never trigger it: the tokenizer already collapsed
     * them, so a {@code ';'} inside {@code '…'} or a {@code --} line comment is
     * not a punctuation token here.
     *
     * <p>This matters because the protocol contract is one statement per
     * {@code sql} field: the native adapter's prepare_v2 compiles only the
     * FIRST statement and silently drops the tail. Without this check a leading
     * no-op — {@code SELECT 1; PRAGMA writable_schema = ON} — anchors
     * {@link #leadingKeyword} / {@link #writeTarget} on the harmless
     * {@code SELECT}, bypassing the forbidden-statement and protocol-table-write
     * denylists entirely, and silently discards the author's real (rejected)
     * statement.</p>
     */
    public static boolean hasTrailingStatement(List<SqlToken> tokens) {
        int depth = 0;
        for (int i = 0; i < tokens.size(); i++) {
            SqlToken token = tokens.get(i);
            if (token.isPunct("(")) {
                depth++;
            } else if (token.isPunct(")")) {
                depth--;
            } else if (depth == 0 && token.isPunct(";")) {
                return i + 1 < tokens.size();
            }
        }
        return false;
    }

    /**
     * The single table an INSERT / UPDATE / DELETE writes, or {@code null}
     * when the statement is not a write — the anchor of the
     * protocol-table-write lint (docs/validation.md).
     *
     * <p>Unlike {@link #parseInsert}, the verb is anchored at the start of the
     * statement (after an optional {@code WITH …} CTE prefix) instead of being
     * matched anywhere in the token stream. That matters because this lint
     * raises an ERROR that blocks publication: a scan-anywhere match would
     * read {@code SELECT "delete" FROM result_x} as a DELETE against
     * {@code result_x} and reject the single most important legal pattern —
     * reading a result table. Skipping the CTE prefix rather than only
     * looking at token 0 is equally load-bearing in the other direction: a
     * bare {@code WITH d AS (SELECT 1) INSERT INTO result_x …} would
     * otherwise slip past the lint entirely.</p>
     *
     * <p>Schema-qualified names keep their last component, mirroring
     * {@link #parseInsert}.</p>
     */
    public static String writeTarget(List<SqlToken> tokens) {
        int pos = skipCtePrefix(tokens);
        if (pos >= tokens.size() || tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
            return null;
        }
        if (tokens.get(pos).isIdent("insert") || tokens.get(pos).isIdent("replace")) {
            pos++;
            // INSERT OR REPLACE / OR IGNORE / … — at most two idents before INTO.
            for (int guard = 0; guard < 2 && pos < tokens.size()
                    && !tokens.get(pos).isIdent("into"); guard++) {
                if (tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
                    return null;
                }
                pos++;
            }
            if (pos >= tokens.size() || !tokens.get(pos).isIdent("into")) {
                return null;
            }
            return qualifiedName(tokens, pos + 1);
        }
        if (tokens.get(pos).isIdent("update")) {
            pos++;
            // UPDATE OR ROLLBACK / OR ABORT / … — one conflict-clause ident.
            if (pos + 1 < tokens.size() && tokens.get(pos).isIdent("or")) {
                pos += 2;
            }
            return qualifiedName(tokens, pos);
        }
        if (tokens.get(pos).isIdent("delete")) {
            pos++;
            if (pos >= tokens.size() || !tokens.get(pos).isIdent("from")) {
                return null;
            }
            return qualifiedName(tokens, pos + 1);
        }
        return null;
    }

    /**
     * Index of the statement verb after an optional {@code WITH [RECURSIVE]}
     * CTE prefix ({@code name [(cols)] AS [[NOT] MATERIALIZED] (body) [, …]}),
     * or 0 when the statement has no such prefix. Each parenthesized group is
     * skipped by a balanced scan, so a CTE body containing its own commas,
     * subqueries, or the word {@code begin} never confuses the walk.
     */
    private static int skipCtePrefix(List<SqlToken> tokens) {
        if (tokens.isEmpty() || !tokens.get(0).isIdent("with")) {
            return 0;
        }
        int pos = 1;
        if (pos < tokens.size() && tokens.get(pos).isIdent("recursive")) {
            pos++;
        }
        while (pos < tokens.size()) {
            if (tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
                return tokens.size(); // unrecognized shape — no write target
            }
            pos++; // CTE name
            if (pos < tokens.size() && tokens.get(pos).isPunct("(")) {
                pos = skipBalanced(tokens, pos); // optional column list
            }
            if (pos < tokens.size() && tokens.get(pos).isIdent("as")) {
                pos++;
            }
            if (pos < tokens.size() && tokens.get(pos).isIdent("not")) {
                pos++;
            }
            if (pos < tokens.size() && tokens.get(pos).isIdent("materialized")) {
                pos++;
            }
            if (pos >= tokens.size() || !tokens.get(pos).isPunct("(")) {
                return tokens.size(); // unrecognized shape — no write target
            }
            pos = skipBalanced(tokens, pos); // CTE body
            if (pos < tokens.size() && tokens.get(pos).isPunct(",")) {
                pos++;
                continue; // another CTE
            }
            return pos;
        }
        return pos;
    }

    /** Index just past the ')' matching the '(' at {@code open}. */
    private static int skipBalanced(List<SqlToken> tokens, int open) {
        int depth = 0;
        for (int pos = open; pos < tokens.size(); pos++) {
            if (tokens.get(pos).isPunct("(")) {
                depth++;
            } else if (tokens.get(pos).isPunct(")")) {
                depth--;
                if (depth == 0) {
                    return pos + 1;
                }
            }
        }
        return tokens.size();
    }

    /** Read {@code [schema.]table} at {@code start}, keeping the last component. */
    private static String qualifiedName(List<SqlToken> tokens, int start) {
        int pos = start;
        if (pos >= tokens.size() || tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
            return null;
        }
        String name = tokens.get(pos).text();
        pos++;
        while (pos + 1 < tokens.size()
                && tokens.get(pos).isPunct(".")
                && tokens.get(pos + 1).kind() == SqlToken.Kind.IDENT) {
            name = tokens.get(pos + 1).text();
            pos += 2;
        }
        return name;
    }

    private static int indexOfIdent(List<SqlToken> tokens, String name) {
        for (int i = 0; i < tokens.size(); i++) {
            if (tokens.get(i).isIdent(name)) {
                return i;
            }
        }
        return -1;
    }
}
