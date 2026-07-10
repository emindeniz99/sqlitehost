using System.IO;
using System.Text;
using Example.Game.Generated;
using SqliteHost.Tests.Fixtures;
using Xunit;

namespace SqliteHost.Tests
{
    public class SchemaGoldenTests
    {
        [Fact]
        public void GenerateSchemaScript_IsByteIdenticalToDdlSnapshotFixture()
        {
            byte[] expected = File.ReadAllBytes(FixturePaths.Schema("sample-host.ddl.sql"));
            string script = GeneratedHostDefinition.Build().GenerateSchemaScript();
            byte[] actual = Encoding.UTF8.GetBytes(script);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void GeneratedSchemaSqlConstant_IsByteIdenticalToDdlSnapshotFixture()
        {
            byte[] expected = File.ReadAllBytes(FixturePaths.Schema("sample-host.ddl.sql"));
            byte[] actual = Encoding.UTF8.GetBytes(GeneratedSchemaSql.SchemaScript);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void GenerateSchemaStatements_JoinedWithBlankLines_EqualsSchemaScript()
        {
            var definition = GeneratedHostDefinition.Build();
            string joined = string.Join("\n\n", definition.GenerateSchemaStatements()) + "\n";
            Assert.Equal(definition.GenerateSchemaScript(), joined);
        }

        [Fact]
        public void GenerateSchemaStatements_FollowsPinnedStatementOrder()
        {
            // pending_host_calls, script_inputs, then per method in declaration
            // order: call table, input list child tables, result table,
            // result list child tables, trigger (docs/workspace-schema.md).
            var statements = GeneratedHostDefinition.Build().GenerateSchemaStatements();
            Assert.Equal(19, statements.Count);
            Assert.StartsWith("CREATE TABLE pending_host_calls (", statements[0]);
            Assert.StartsWith("CREATE TABLE script_inputs (", statements[1]);
            Assert.StartsWith("CREATE TABLE call_get_value (", statements[2]);
            Assert.StartsWith("CREATE TABLE result_get_value (", statements[3]);
            Assert.StartsWith("CREATE TRIGGER trg_call_get_value_queue", statements[4]);
            Assert.StartsWith("CREATE TABLE call_set_value (", statements[5]);
            Assert.StartsWith("CREATE TABLE result_set_value (", statements[6]);
            Assert.StartsWith("CREATE TRIGGER trg_call_set_value_queue", statements[7]);
            Assert.StartsWith("CREATE TABLE call_get_values (", statements[8]);
            Assert.StartsWith("CREATE TABLE call_get_values__input_keys (", statements[9]);
            Assert.StartsWith("CREATE TABLE result_get_values (", statements[10]);
            Assert.StartsWith("CREATE TABLE result_get_values__result_entries (", statements[11]);
            Assert.StartsWith("CREATE TRIGGER trg_call_get_values_queue", statements[12]);
            Assert.StartsWith("CREATE TABLE call_put_blob (", statements[13]);
            Assert.StartsWith("CREATE TABLE result_put_blob (", statements[14]);
            Assert.StartsWith("CREATE TRIGGER trg_call_put_blob_queue", statements[15]);
            Assert.StartsWith("CREATE TABLE call_record_score (", statements[16]);
            Assert.StartsWith("CREATE TABLE result_record_score (", statements[17]);
            Assert.StartsWith("CREATE TRIGGER trg_call_record_score_queue", statements[18]);
        }

        [Fact]
        public void SupportedFeatures_ArePinnedProtocolV1Features()
        {
            var definition = GeneratedHostDefinition.Build();
            Assert.Equal(
                new[] { "typedNamedBindings", "splitResultTables", "scriptInputs" },
                definition.SupportedFeatures);
        }

        [Fact]
        public void GeneratedDefinition_ExposesManifestApiLevelAndMethods()
        {
            var definition = GeneratedHostDefinition.Build();
            Assert.Equal(1, definition.ApiLevel);
            Assert.Equal(5, definition.Methods.Count);
            Assert.Equal("getValue", definition.Methods[0].MethodName);
            Assert.Equal("setValue", definition.Methods[1].MethodName);
            Assert.Equal("getValues", definition.Methods[2].MethodName);
            Assert.Equal("putBlob", definition.Methods[3].MethodName);
            Assert.Equal("recordScore", definition.Methods[4].MethodName);
            Assert.All(definition.Methods, method => Assert.Equal(1, method.ApiLevel));
        }
    }
}
