#if !SQLITEHOST_SLIM
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Lexical scanner for named SQL parameters (docs/errors.md "Binding
    /// validation"). Finds :name / @name / $name references while skipping
    /// string literals ('…' with '' escapes) and quoted identifiers —
    /// double-quoted ("…" with "" escapes), bracket ([…], ends at the
    /// first ']', no escape) and backtick (`…` with `` escapes) — plus
    /// line comments (--) and block comments
    /// (/* */). A parameter's name runs over SQLite's own IdChar set
    /// (letters, digits, '_', '$', and any character above 0x7f), so a
    /// non-ASCII name is read whole rather than cut at its ASCII head —
    /// the adapter conformance suite requires such names to bind. Per the
    /// pinned rule, '$' is also an identifier character in SQLite, so a
    /// '$' immediately preceded by an identifier character continues that
    /// identifier instead of starting a parameter; ':' and '@' always
    /// start parameters outside quoted regions. The same algorithm is used
    /// by the Java validator and the TypeScript authoring lint.
    /// </summary>
    internal static class SqlParameterScanner
    {
        /// <summary>Bare parameter names, distinct, in first-occurrence order.</summary>
        public static List<string> ScanParameterNames(string sql)
        {
            var names = new List<string>();
            int i = 0;
            int length = sql.Length;
            bool previousIsIdentifierChar = false;
            while (i < length)
            {
                char ch = sql[i];
                if (ch == '\'')
                {
                    i = SkipQuoted(sql, i, '\'');
                    previousIsIdentifierChar = false;
                }
                else if (ch == '"')
                {
                    i = SkipQuoted(sql, i, '"');
                    previousIsIdentifierChar = false;
                }
                else if (ch == '`')
                {
                    // Backtick-quoted identifier (MySQL compat): doubled
                    // backtick escapes, same shape as SkipQuoted.
                    i = SkipQuoted(sql, i, '`');
                    previousIsIdentifierChar = false;
                }
                else if (ch == '[')
                {
                    // Bracket-quoted identifier (MS Access/SQL Server
                    // compat): no escape — ends at the first ']'.
                    i = SkipBracket(sql, i);
                    previousIsIdentifierChar = false;
                }
                else if (ch == '-' && i + 1 < length && sql[i + 1] == '-')
                {
                    i = SkipLineComment(sql, i);
                    previousIsIdentifierChar = false;
                }
                else if (ch == '/' && i + 1 < length && sql[i + 1] == '*')
                {
                    i = SkipBlockComment(sql, i);
                    previousIsIdentifierChar = false;
                }
                else if (ch == ':' || ch == '@' || (ch == '$' && !previousIsIdentifierChar))
                {
                    int start = i + 1;
                    int end = start;
                    while (end < length && IsSqlIdentifierChar(sql[end]))
                    {
                        end++;
                    }
                    if (end > start)
                    {
                        string name = sql.Substring(start, end - start);
                        if (!names.Contains(name))
                        {
                            names.Add(name);
                        }
                        i = end;
                        previousIsIdentifierChar = true;
                    }
                    else
                    {
                        i++;
                        previousIsIdentifierChar = IsSqlIdentifierChar(ch);
                    }
                }
                else
                {
                    // '$' preceded by an identifier character falls through
                    // here: it continues the identifier run instead of
                    // starting a parameter.
                    previousIsIdentifierChar = IsSqlIdentifierChar(ch);
                    i++;
                }
            }
            return names;
        }

        private static int SkipQuoted(string sql, int start, char quote)
        {
            int i = start + 1;
            while (i < sql.Length)
            {
                if (sql[i] == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote)
                    {
                        i += 2;
                        continue;
                    }
                    return i + 1;
                }
                i++;
            }
            return i;
        }

        private static int SkipBracket(string sql, int start)
        {
            int i = start + 1;
            while (i < sql.Length && sql[i] != ']')
            {
                i++;
            }
            return i < sql.Length ? i + 1 : i;
        }

        private static int SkipLineComment(string sql, int start)
        {
            int i = start + 2;
            while (i < sql.Length && sql[i] != '\n')
            {
                i++;
            }
            return i;
        }

        private static int SkipBlockComment(string sql, int start)
        {
            int i = start + 2;
            while (i + 1 < sql.Length)
            {
                if (sql[i] == '*' && sql[i + 1] == '/')
                {
                    return i + 2;
                }
                i++;
            }
            return sql.Length;
        }

        private static bool IsIdentifierChar(char ch)
        {
            return (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch == '_';
        }

        /// <summary>
        /// SQLite's IdChar (docs/errors.md pinned rule): letters, digits,
        /// '_', '$', and any character above 0x7f.
        /// </summary>
        private static bool IsSqlIdentifierChar(char ch)
        {
            return IsIdentifierChar(ch) || ch == '$' || ch > '\u007f';
        }
    }
}
#endif
