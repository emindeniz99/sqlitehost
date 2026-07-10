using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SqliteHost.Tests.Fixtures
{
    /// <summary>
    /// Test-only JSON loader for fixture payloads (docs/script-envelope.md).
    /// JSON parsing is deliberately not part of the core C# runtime; the
    /// runtime consumes parsed <see cref="SqliteHostScript"/> objects.
    /// </summary>
    public static class ScriptEnvelopeJson
    {
        public static SqliteHostScript LoadPayload(string relativePath)
        {
            return Parse(File.ReadAllText(FixturePaths.Payload(relativePath)));
        }

        public static SqliteHostScript Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            var script = new SqliteHostScript
            {
                Engine = GetString(root, "engine"),
                ScriptId = GetString(root, "scriptId"),
                RequiredApiLevel = root.TryGetProperty("requiredApiLevel", out JsonElement apiLevel)
                    ? apiLevel.GetInt32()
                    : 0,
                RequiredFeatures = GetStringList(root, "requiredFeatures"),
                RequiredMethods = GetStringList(root, "requiredMethods"),
                Steps = new List<SqliteHostStep>()
            };

            if (root.TryGetProperty("inputs", out JsonElement inputs))
            {
                script.Inputs = new List<SqliteHostRuntimeInput>();
                foreach (JsonElement input in inputs.EnumerateArray())
                {
                    script.Inputs.Add(new SqliteHostRuntimeInput
                    {
                        Name = GetString(input, "name"),
                        Value = ParseBindingValue(input.GetProperty("value"))
                    });
                }
            }

            foreach (JsonElement step in root.GetProperty("steps").EnumerateArray())
            {
                var parsedStep = new SqliteHostStep
                {
                    Id = GetString(step, "id"),
                    Statements = new List<SqliteHostStatement>()
                };
                foreach (JsonElement statement in step.GetProperty("statements").EnumerateArray())
                {
                    var parsedStatement = new SqliteHostStatement
                    {
                        Sql = GetString(statement, "sql")
                    };
                    if (statement.TryGetProperty("bindings", out JsonElement bindings))
                    {
                        parsedStatement.Bindings = new Dictionary<string, SqliteHostBindingValue>();
                        foreach (JsonProperty binding in bindings.EnumerateObject())
                        {
                            parsedStatement.Bindings.Add(binding.Name, ParseBindingValue(binding.Value));
                        }
                    }
                    parsedStep.Statements.Add(parsedStatement);
                }
                script.Steps.Add(parsedStep);
            }
            return script;
        }

        private static SqliteHostBindingValue ParseBindingValue(JsonElement element)
        {
            string type = element.GetProperty("type").GetString();
            switch (type)
            {
                case "null":
                    return SqliteHostBindingValue.Null();
                case "int32":
                    return SqliteHostBindingValue.Int32(ParseInt32(element.GetProperty("value")));
                case "int64":
                    return SqliteHostBindingValue.Int64(ParseInt64(element.GetProperty("value")));
                case "bool":
                    return SqliteHostBindingValue.Bool(element.GetProperty("value").GetBoolean());
                case "text":
                    return SqliteHostBindingValue.Text(element.GetProperty("value").GetString());
                case "blob":
                    return SqliteHostBindingValue.Blob(Convert.FromBase64String(element.GetProperty("value").GetString()));
                case "float32":
                    // float32/float64 are JSON numbers only (string form is
                    // rejected); float32 rounds to nearest single.
                    return SqliteHostBindingValue.Float32((float)element.GetProperty("value").GetDouble());
                case "float64":
                    return SqliteHostBindingValue.Float64(element.GetProperty("value").GetDouble());
                default:
                    throw new InvalidDataException("Unknown binding value type '" + type + "'.");
            }
        }

        private static int ParseInt32(JsonElement value)
        {
            // int32 accepts a JSON number or a decimal string.
            return value.ValueKind == JsonValueKind.String
                ? int.Parse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture)
                : value.GetInt32();
        }

        private static long ParseInt64(JsonElement value)
        {
            // int64 accepts a JSON number when |v| <= 2^53-1, else a decimal string.
            return value.ValueKind == JsonValueKind.String
                ? long.Parse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture)
                : value.GetInt64();
        }

        private static string GetString(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
        }

        private static List<string> GetStringList(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value))
            {
                return null;
            }
            var list = new List<string>();
            foreach (JsonElement entry in value.EnumerateArray())
            {
                list.Add(entry.GetString());
            }
            return list;
        }
    }
}
