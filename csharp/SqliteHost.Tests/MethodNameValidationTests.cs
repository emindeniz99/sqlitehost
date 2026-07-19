using System;
using System.Text.RegularExpressions;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Build-time validation of method names (docs/naming.md): a registered
    /// method name must be a valid protocol identifier
    /// ([A-Za-z][A-Za-z0-9_]*), mirroring the canonical TypeSpec METHOD_NAME
    /// rule. Without the guard an invalid name registers cleanly and then
    /// fails EVERY Run at schema creation, because the runtime interpolates
    /// the derived call/result/trigger table name — and the raw method name
    /// into a trigger string literal — unquoted, producing malformed
    /// (FailedSchema) or injection-prone DDL far from the code that authored
    /// the spec. All three registration surfaces lower to the same erased
    /// core, so the check is validated in SqliteHostDefinitionCore and holds
    /// surface-independently.
    /// </summary>
    public class MethodNameValidationTests
    {
        private sealed class KeyInput
        {
            public string Key { get; set; }
        }

        private sealed class ValueResult
        {
            public long Value { get; set; }
        }

        private static SqliteHostDefinition<object> Define(params IHostMethodSpec<object>[] specs)
        {
            return SqliteHostDefinition.ForHandlers<object>().Methods(specs);
        }

        private static IHostMethodSpec<object> Spec(string methodName)
        {
            return HostMethod
                .For<object, KeyInput, ValueResult>(methodName)
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((h, input) => new ValueResult())
                .Build();
        }

        [Fact]
        public void MethodNameWithHyphen_ThrowsAtBuildTime()
        {
            // "get-value" snake-cases to itself and yields
            // `CREATE TABLE call_get-value (...)` (a SQLite syntax error), so
            // without this guard the definition builds but every Run fails
            // FailedSchema at first schema creation.
            var ex = Assert.Throws<ArgumentException>(() => Define(Spec("get-value")));
            Assert.Contains("get-value", ex.Message);
            Assert.Contains("not a valid identifier", ex.Message);
        }

        [Fact]
        public void MethodNameWithApostrophe_ThrowsAtBuildTime()
        {
            // The raw method name is interpolated UNESCAPED into the queue
            // trigger's string literal (unlike DoneValue, which is escaped),
            // so "bad'name" produces a broken/injection-prone `'bad'name'`.
            // The guard stops it at registration.
            var ex = Assert.Throws<ArgumentException>(() => Define(Spec("bad'name")));
            Assert.Contains("bad'name", ex.Message);
            Assert.Contains("not a valid identifier", ex.Message);
        }

        [Fact]
        public void MethodNameStartingWithDigit_ThrowsAtBuildTime()
        {
            // Pins the ^[A-Za-z] first-char rule: a leading digit is a valid
            // table-suffix character but not a valid identifier start.
            var ex = Assert.Throws<ArgumentException>(() => Define(Spec("1st")));
            Assert.Contains("1st", ex.Message);
            Assert.Contains("not a valid identifier", ex.Message);
        }

        [Fact]
        public void Ultra_InvalidMethodName_ThrowsAtBuildTime()
        {
            // The ultra registration surface has its own builder but lowers to
            // the same erased core, so the DefinitionCore check must reject an
            // invalid name here too (surface-independence).
            var spec = UltraHostMethod
                .For<object>("get-value")
                .InputText("key")
                .ResultLong("value")
                .Handler((h, call) => new SqliteHostUltraResult().SetInt64("value", 1))
                .Build();

            var ex = Assert.Throws<ArgumentException>(() => Define(spec));
            Assert.Contains("get-value", ex.Message);
            Assert.Contains("not a valid identifier", ex.Message);
        }

        [Theory]
        [InlineData("getValue")]
        [InlineData("get_value")]
        public void ValidMethodName_Builds(string methodName)
        {
            // camelCase and snake_case are both valid identifiers under
            // METHOD_NAME=/^[A-Za-z][A-Za-z0-9_]*$/ — a guard against
            // over-tightening. (Each is built in its own definition because
            // "getValue" and "get_value" both derive the call_get_value
            // table.)
            var definition = Define(Spec(methodName));
            Assert.NotEmpty(definition.GenerateSchemaStatements());
        }

        [Theory]
        [InlineData("getValue")]
        [InlineData("get_value")]
        [InlineData("A")]
        [InlineData("z9")]
        [InlineData("with_1_and_UPPER")]
        [InlineData("get-value")]
        [InlineData("bad'name")]
        [InlineData("1st")]
        [InlineData("_leading")]
        [InlineData("")]
        [InlineData("has space")]
        [InlineData("dollar$sign")]
        public void CharScan_MatchesSharedMethodNamePattern(string name)
        {
            // The runtime deliberately hand-rolls the method-name check
            // (no System.Text.RegularExpressions on netstandard2.0/Unity),
            // so it can silently drift from the canonical pattern. Pin the
            // scan to the single source (ir.ts METHOD_NAME_PATTERN, projected
            // into ProtocolConstants.MethodNamePattern) by requiring it to
            // agree, verdict for verdict, with that pattern compiled as a
            // real Regex — the one place we're allowed to use one, in a test.
            bool regexVerdict = Regex.IsMatch(name, ProtocolConstants.MethodNamePattern);
            bool scanVerdict = SqliteHostDefinitionCore.IsValidMethodName(name);
            Assert.Equal(regexVerdict, scanVerdict);
        }
    }
}
