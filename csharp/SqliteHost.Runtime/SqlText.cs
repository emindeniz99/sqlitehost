namespace SqliteHost
{
    /// <summary>
    /// The one pinned whitespace set, and blankness decided on it.
    ///
    /// <para>docs/script-envelope.md fixes it by enumeration — space,
    /// <c>\t</c>, <c>\n</c>, <c>\v</c>, <c>\f</c>, <c>\r</c>, C's
    /// <c>isspace()</c> — rather than by each language's own idea of
    /// whitespace, because those differ and an envelope must be rejected
    /// identically everywhere. The SQL parameter scanner needs the same
    /// set, so it lives here: one definition, two callers.</para>
    ///
    /// <para>It is deliberately NOT inside
    /// <see cref="SqlParameterScanner"/>, which is validation-only and
    /// compiles out of <c>SQLITEHOST_SLIM</c> builds (the vendoring tool
    /// drops the file outright). The envelope's non-blank rule is not a
    /// strict check that a slim build may skip.</para>
    /// </summary>
    internal static class SqlText
    {
        /// <summary>The pinned set, and nothing else — not char.IsWhiteSpace.</summary>
        public static bool IsSqlWhitespace(char ch)
        {
            return ch == ' ' || ch == '\t' || ch == '\n' || ch == '\u000b' || ch == '\f' || ch == '\r';
        }

        /// <summary>True for null, empty, or only <see cref="IsSqlWhitespace"/> characters.</summary>
        public static bool IsBlank(string value)
        {
            if (value == null)
            {
                return true;
            }
            for (int i = 0; i < value.Length; i++)
            {
                if (!IsSqlWhitespace(value[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
