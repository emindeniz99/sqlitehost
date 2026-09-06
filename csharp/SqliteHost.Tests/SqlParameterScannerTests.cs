#if !SQLITEHOST_SLIM
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// The lexical parameter scanner is one of the optional strict checks
    /// SQLITEHOST_SLIM strips (docs/csharp-api.md, SQLITEHOST_SLIM), so the
    /// whole class compiles out with it — <c>SqlParameterScanner</c> does
    /// not exist in a slim build.
    /// </summary>
    public class SqlParameterScannerTests
    {
        [Fact]
        public void FindsAllThreePrefixes()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT :alpha, @beta, $gamma FROM t");
            Assert.Equal(new[] { "alpha", "beta", "gamma" }, names);
        }

        [Fact]
        public void ReturnsDistinctNamesInFirstOccurrenceOrder()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT :b, :a, @b, $a FROM t");
            Assert.Equal(new[] { "b", "a" }, names);
        }

        [Fact]
        public void SkipsStringLiterals_IncludingEscapedQuotes()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "INSERT INTO t (a) VALUES (':notAParam') -- trailing\n"
                + "UPDATE t SET a = 'it''s :also not one' WHERE b = :real");
            Assert.Equal(new[] { "real" }, names);
        }

        [Fact]
        public void SkipsDoubleQuotedIdentifiers()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT \":ghost\", \"a\"\"b :ghost2\" FROM t WHERE x = @real");
            Assert.Equal(new[] { "real" }, names);
        }

        [Fact]
        public void SkipsBracketQuotedIdentifiers()
        {
            // [id] (MS Access/SQL Server compat): a ':' inside a bracket
            // identifier is not a parameter (it ends at the first ']'), and
            // a real parameter after it is still found.
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT [weird:col] FROM t WHERE x = :real");
            Assert.Equal(new[] { "real" }, names);
        }

        [Fact]
        public void SkipsBacktickQuotedIdentifiers()
        {
            // `id` (MySQL compat): a '$' inside a backtick identifier is not
            // a parameter, doubled backticks are escapes, and a real
            // parameter after it is still found.
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT `x$y`, `a``b :ghost` FROM t WHERE z = $real");
            Assert.Equal(new[] { "real" }, names);
        }

        [Fact]
        public void SkipsLineComments()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT 1 -- uses :ghost here\nFROM t WHERE x = :real");
            Assert.Equal(new[] { "real" }, names);
        }

        [Fact]
        public void SkipsBlockComments()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT 1 /* :ghost and\n @ghost2 */ FROM t WHERE x = $real");
            Assert.Equal(new[] { "real" }, names);
        }

        [Fact]
        public void UnterminatedBlockCommentOrLiteral_SwallowsRest()
        {
            Assert.Empty(SqlParameterScanner.ScanParameterNames("SELECT 1 /* :ghost"));
            Assert.Empty(SqlParameterScanner.ScanParameterNames("SELECT ':ghost"));
        }

        [Fact]
        public void PrefixWithoutIdentifier_IsNotAParameter()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT a :: b, '$' , x FROM t WHERE y = :ok");
            Assert.Equal(new[] { "ok" }, names);
        }

        [Fact]
        public void ParameterNamesAllowDigitsAndUnderscores()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT :v0, :snake_name_2 FROM t");
            Assert.Equal(new[] { "v0", "snake_name_2" }, names);
        }

        [Fact]
        public void ParameterNamesAllowNonAsciiIdentifierChars()
        {
            // SQLite's IdChar counts every character above 0x7f, and the
            // adapter conformance suite makes non-ASCII parameter names a
            // contract requirement (AdapterConformanceTestsBase,
            // UnicodeParameterName_ResolvesAndBinds). Scanning only the
            // ASCII head would split ":anahtarIsmi" into a name the author
            // never wrote, and binding validation would then reject a
            // statement the engine binds correctly.
            var names = SqlParameterScanner.ScanParameterNames(
                "INSERT INTO scratch (a, b) VALUES (:anahtarİsmi, @слово)");
            Assert.Equal(new[] { "anahtarİsmi", "слово" }, names);
        }

        [Fact]
        public void ParameterNamesAllowDollarInsideTheName()
        {
            // '$' is an identifier character, so it continues a parameter
            // name it sits inside just as it continues a column name.
            Assert.Equal(new[] { "a$b" }, SqlParameterScanner.ScanParameterNames("SELECT :a$b"));
        }

        [Fact]
        public void DollarPrecededByIdentifierChar_ContinuesIdentifier()
        {
            Assert.Empty(SqlParameterScanner.ScanParameterNames(
                "CREATE TABLE t_x (a$b INTEGER)"));
        }

        [Fact]
        public void DollarInsideIdentifier_DoesNotHideRealParameters()
        {
            var names = SqlParameterScanner.ScanParameterNames(
                "SELECT foo$bar, :real FROM t");
            Assert.Equal(new[] { "real" }, names);
        }

        [Fact]
        public void DollarAtTokenBoundary_IsAParameter()
        {
            Assert.Equal(new[] { "v" }, SqlParameterScanner.ScanParameterNames("$v"));
        }

        [Fact]
        public void DollarIdentifierThenDollarParameter_FindsOnlyTheParameter()
        {
            Assert.Equal(new[] { "c" }, SqlParameterScanner.ScanParameterNames("a$b then $c"));
        }

        [Fact]
        public void ParameterNamesCarrySqlitesTclVariableSuffixes()
        {
            // SQLite's variable syntax admits a doubled colon inside the name
            // and one trailing '(...)' group, for every prefix — verified
            // against the sqlite3 CLI 3.51.0, where `SELECT :a::b` reports a
            // missing value for the binding parameter `:a::b`, not for `:a`.
            // The Java validator and the TypeScript authoring lint scan it
            // this way; a runtime that splits the name rejects at run time
            // exactly the payloads they accept.
            Assert.Equal(new[] { "a::b" }, SqlParameterScanner.ScanParameterNames("SELECT :a::b"));
            Assert.Equal(new[] { "x::y" }, SqlParameterScanner.ScanParameterNames("SELECT @x::y"));
            Assert.Equal(new[] { "a::b::c" }, SqlParameterScanner.ScanParameterNames("SELECT :a::b::c"));
            Assert.Equal(new[] { "a::" }, SqlParameterScanner.ScanParameterNames("SELECT :a::"));
            Assert.Equal(new[] { "a(1)" }, SqlParameterScanner.ScanParameterNames("SELECT $a(1)"));
            Assert.Equal(new[] { "a(1)" }, SqlParameterScanner.ScanParameterNames("SELECT :a(1)"));
            Assert.Equal(new[] { "a()" }, SqlParameterScanner.ScanParameterNames("SELECT $a()"));
            Assert.Equal(new[] { "a::b(1)" }, SqlParameterScanner.ScanParameterNames("SELECT $a::b(1)"));
        }

        [Fact]
        public void NeighbouringVariableFormsSqliteRejectsStayRejected()
        {
            // Each of these is a tokenizer error in the engine, so no scanner
            // reading may invent a well-formed parameter out of them: ":a:b"
            // is two adjacent variables (a parse error), "::a" and "$(1)"
            // have no IdChar to name, and a '(' group that does not close on
            // the same token is illegal. Same expectations the Java
            // tokenizer tests pin.
            Assert.Equal(new[] { "a", "b" }, SqlParameterScanner.ScanParameterNames("SELECT :a:b"));
            Assert.Equal(new[] { "a" }, SqlParameterScanner.ScanParameterNames("SELECT ::a"));
            Assert.Empty(SqlParameterScanner.ScanParameterNames("SELECT $(1)"));
            Assert.Empty(SqlParameterScanner.ScanParameterNames("SELECT $a( 1)"));
            Assert.Empty(SqlParameterScanner.ScanParameterNames("SELECT $a(1"));
        }
    }
}
#endif
