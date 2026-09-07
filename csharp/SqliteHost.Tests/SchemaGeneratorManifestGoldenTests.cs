using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using SqliteHost.Tests.Fixtures;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// The C# DDL generator against every committed manifest, not only the
    /// host whose generated definition <see cref="SchemaGoldenTests"/>
    /// builds.
    ///
    /// SchemaGoldenTests can only ever exercise <c>sample-host</c>: it goes
    /// through the emitted <c>GeneratedHostDefinition</c>, so it covers one
    /// host and one naming configuration — the protocol defaults. That
    /// leaves the interesting half of SchemaGenerator untested. A copy of
    /// the naming rules that hardcoded "input_", "call_id" or
    /// "pending_host_calls" instead of reading the definition emits exactly
    /// the sample host's committed bytes, so the golden passes on the
    /// wrong code.
    ///
    /// <c>custom-naming-host</c> is the manifest that tells the two apart:
    /// every prefix, infix, shared table name, shared column name and the
    /// done-status literal differ from their defaults. The same file is
    /// pinned by tests/cross-language-golden/run.mjs for TypeScript and by
    /// Java's DdlGeneratorGoldenTest, so one input measures all three
    /// generators.
    ///
    /// A failure here is not "update the expectation": either the
    /// canonical generator in codegen/core/src/ddl.ts moved and this copy
    /// did not, or this copy drifted.
    /// </summary>
    public class SchemaGeneratorManifestGoldenTests
    {
        [Theory]
        [InlineData("sample-host")]
        [InlineData("high-api-host")]
        [InlineData("syntax-floor-host")]
        [InlineData("custom-naming-host")]
        public void GenerateScript_IsByteIdenticalToTheDdlSnapshot(string baseName)
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(FixturePaths.Manifest(baseName + ".manifest.json")));
            JsonElement manifest = document.RootElement;

            string script = SchemaGenerator.GenerateScript(
                NamingOf(manifest),
                ColumnsOf(manifest),
                MethodsOf(manifest));

            byte[] expected = File.ReadAllBytes(FixturePaths.Schema(baseName + ".ddl.sql"));
            Assert.Equal(expected, Encoding.UTF8.GetBytes(script));
        }

        private static SqliteHostNaming NamingOf(JsonElement manifest)
        {
            JsonElement naming = manifest.GetProperty("naming");
            return new SqliteHostNaming(
                naming.GetProperty("callTablePrefix").GetString(),
                naming.GetProperty("resultTablePrefix").GetString(),
                naming.GetProperty("inputColumnPrefix").GetString(),
                naming.GetProperty("resultColumnPrefix").GetString(),
                naming.GetProperty("inputListTableInfix").GetString(),
                naming.GetProperty("resultListTableInfix").GetString(),
                manifest.GetProperty("queueTable").GetProperty("name").GetString(),
                manifest.GetProperty("inputsTable").GetProperty("name").GetString(),
                manifest.GetProperty("varsTable").GetProperty("name").GetString(),
                manifest.GetProperty("controlTable").GetProperty("name").GetString(),
                naming.GetProperty("functionPrefix").GetString());
        }

        private static SqliteHostColumns ColumnsOf(JsonElement manifest)
        {
            JsonElement columns = manifest.GetProperty("columns");
            return new SqliteHostColumns(
                columns.GetProperty("callId").GetString(),
                columns.GetProperty("itemIndex").GetString(),
                columns.GetProperty("status").GetString(),
                columns.GetProperty("doneValue").GetString(),
                columns.GetProperty("queueId").GetString(),
                columns.GetProperty("method").GetString(),
                columns.GetProperty("name").GetString(),
                columns.GetProperty("valueType").GetString(),
                columns.GetProperty("intValue").GetString(),
                columns.GetProperty("realValue").GetString(),
                columns.GetProperty("textValue").GetString(),
                columns.GetProperty("blobValue").GetString(),
                columns.GetProperty("action").GetString(),
                columns.GetProperty("message").GetString());
        }

        private static List<SchemaMethodModel> MethodsOf(JsonElement manifest)
        {
            var methods = new List<SchemaMethodModel>();
            foreach (JsonElement method in manifest.GetProperty("methods").EnumerateArray())
            {
                JsonElement input = method.GetProperty("input");
                JsonElement result = method.GetProperty("result");
                methods.Add(new SchemaMethodModel(
                    method.GetProperty("methodName").GetString(),
                    FieldsOf(input),
                    ListFieldsOf(input),
                    FieldsOf(result),
                    ListFieldsOf(result),
                    method.GetProperty("callTable").GetString(),
                    method.GetProperty("resultTable").GetString(),
                    method.GetProperty("queueTrigger").GetString()));
            }
            return methods;
        }

        private static List<SchemaFieldModel> FieldsOf(JsonElement shape)
        {
            var fields = new List<SchemaFieldModel>();
            foreach (JsonElement field in shape.GetProperty("fields").EnumerateArray())
            {
                fields.Add(FieldOf(field));
            }
            return fields;
        }

        private static List<SchemaListFieldModel> ListFieldsOf(JsonElement shape)
        {
            var listFields = new List<SchemaListFieldModel>();
            foreach (JsonElement listField in shape.GetProperty("listFields").EnumerateArray())
            {
                var itemFields = new List<SchemaFieldModel>();
                foreach (JsonElement item in listField.GetProperty("itemFields").EnumerateArray())
                {
                    itemFields.Add(FieldOf(item));
                }
                listFields.Add(new SchemaListFieldModel(
                    listField.GetProperty("sqlName").GetString(),
                    itemFields,
                    listField.GetProperty("childTable").GetString()));
            }
            return listFields;
        }

        private static SchemaFieldModel FieldOf(JsonElement field)
        {
            return new SchemaFieldModel(
                field.GetProperty("sqlName").GetString(),
                ScalarTypeOf(field.GetProperty("scalarType").GetString()),
                field.GetProperty("optional").GetBoolean(),
                field.GetProperty("column").GetString());
        }

        private static HostScalarType ScalarTypeOf(string scalarType)
        {
            switch (scalarType)
            {
                case "int32": return HostScalarType.Int32;
                case "int64": return HostScalarType.Int64;
                case "boolean": return HostScalarType.Boolean;
                case "string": return HostScalarType.String;
                case "bytes": return HostScalarType.Bytes;
                case "float32": return HostScalarType.Float32;
                case "float64": return HostScalarType.Float64;
                default: throw new ArgumentOutOfRangeException(nameof(scalarType), scalarType, null);
            }
        }
    }
}
