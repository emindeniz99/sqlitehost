using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SqliteHost.Tests
{
    public class NamingTests
    {
        public class Empty
        {
        }

        public interface INamingHandlers
        {
        }

        private static IHostMethodSpec<INamingHandlers> Spec(string methodName)
        {
            return HostMethod
                .For<INamingHandlers, Empty, Empty>(methodName)
                .Handler((handlers, input) => new Empty())
                .Build();
        }

        [Theory]
        [InlineData("getValue", "call_get_value")]
        [InlineData("defaultValue", "call_default_value")]
        [InlineData("HTTPServer", "call_http_server")]
        [InlineData("putBlob2X", "call_put_blob2_x")]
        public void SnakeCaseRule_MatchesCanonicalExamples(string methodName, string expectedCallTable)
        {
            var definition = SqliteHostDefinition
                .ForHandlers<INamingHandlers>()
                .Methods(new[] { Spec(methodName) });

            var statements = definition.GenerateSchemaStatements();
            Assert.Contains(statements, s => s.StartsWith("CREATE TABLE " + expectedCallTable + " ("));
        }

        [Fact]
        public void DefaultNaming_HasPinnedProtocolV1Prefixes()
        {
            SqliteHostNaming naming = SqliteHostNaming.Default;
            Assert.Equal("call_", naming.CallTablePrefix);
            Assert.Equal("result_", naming.ResultTablePrefix);
            Assert.Equal("input_", naming.InputColumnPrefix);
            Assert.Equal("result_", naming.ResultColumnPrefix);
            Assert.Equal("__input_", naming.InputListTableInfix);
            Assert.Equal("__result_", naming.ResultListTableInfix);
        }

        [Fact]
        public void CustomNaming_PropagatesToEveryDerivedTableColumnAndTrigger()
        {
            var spec = HostMethod
                .For<INamingHandlers, TestSupport.EveryTypeInput, TestSupport.EveryTypeResult>("getValues")
                .Inputs(i => i
                    .OptionalLong("default_value", (x, v) => x.OptI64 = v)
                    .List<TestSupport.PairItem>("keys", (x, v) => x.Pairs = v, item => item
                        .Text("key", (p, v) => p.K = v)))
                .Results(r => r
                    .List<TestSupport.PairItem>("entries", x => x.EchoPairs, item => item
                        .Text("key", p => p.K)))
                .Handler((handlers, input) => new TestSupport.EveryTypeResult())
                .Build();

            var definition = SqliteHostDefinition
                .ForHandlers<INamingHandlers>()
                .Naming(n => n
                    .CallTablePrefix("c_")
                    .ResultTablePrefix("r_")
                    .InputColumnPrefix("in_")
                    .ResultColumnPrefix("out_")
                    .InputListTableInfix("__il_")
                    .ResultListTableInfix("__rl_"))
                .Methods(new[] { spec });

            Assert.Equal("c_", definition.Naming.CallTablePrefix);

            List<string> statements = definition.GenerateSchemaStatements().ToList();
            string script = string.Join("\n\n", statements) + "\n";

            Assert.Contains("CREATE TABLE c_get_values (", script);
            Assert.Contains("    in_default_value INTEGER\n", script);
            Assert.Contains("CREATE TABLE c_get_values__il_keys (", script);
            Assert.Contains("    in_key TEXT NOT NULL,", script);
            Assert.Contains("CREATE TABLE r_get_values (", script);
            Assert.Contains("CREATE TABLE r_get_values__rl_entries (", script);
            Assert.Contains("    out_key TEXT NOT NULL,", script);
            Assert.Contains("CREATE TRIGGER trg_c_get_values_queue\nAFTER INSERT ON c_get_values", script);
        }

        [Fact]
        public void OptionalFields_AreNullable_RequiredFields_AreNotNull()
        {
            var definition = Example.Game.Generated.GeneratedHostDefinition.Build();
            string script = definition.GenerateSchemaScript();
            Assert.Contains("    input_key TEXT NOT NULL\n", script);            // required scalar
            Assert.Contains("    input_default_value INTEGER\n", script);        // optional scalar, no NOT NULL
            Assert.Contains("    input_note TEXT\n", script);                    // optional scalar, no NOT NULL
            Assert.Contains("    input_data BLOB NOT NULL,", script);            // required bytes
        }
    }
}
