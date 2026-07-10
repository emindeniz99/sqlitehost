using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Lexical scanner for named SQL parameters (docs/errors.md "Binding
    /// validation"). Finds :name / @name / $name references while skipping
    /// string literals ('…' with '' escapes), double-quoted identifiers
    /// ("…" with "" escapes), line comments (--) and block comments
    /// (/* */). The same algorithm is used by the Java validator and the
    /// TypeScript authoring lint.
    /// </summary>
    internal static class SqlParameterScanner
    {
        /// <summary>Bare parameter names, distinct, in first-occurrence order.</summary>
        public static List<string> ScanParameterNames(string sql)
        {
            var names = new List<string>();
            int i = 0;
            int length = sql.Length;
            while (i < length)
            {
                char ch = sql[i];
                if (ch == '\'')
                {
                    i = SkipQuoted(sql, i, '\'');
                }
                else if (ch == '"')
                {
                    i = SkipQuoted(sql, i, '"');
                }
                else if (ch == '-' && i + 1 < length && sql[i + 1] == '-')
                {
                    i = SkipLineComment(sql, i);
                }
                else if (ch == '/' && i + 1 < length && sql[i + 1] == '*')
                {
                    i = SkipBlockComment(sql, i);
                }
                else if (ch == ':' || ch == '@' || ch == '$')
                {
                    int start = i + 1;
                    int end = start;
                    while (end < length && IsIdentifierChar(sql[end]))
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
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
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
    }
}
