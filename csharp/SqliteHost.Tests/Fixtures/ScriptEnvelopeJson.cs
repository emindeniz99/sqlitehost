using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        /// <summary>
        /// An object may not repeat a key (docs/script-envelope.md), and
        /// <see cref="JsonDocument"/> resolves one silently as last-wins.
        /// The rule exists because the delivery path's security model is
        /// that the validator judged the same document the device runs, so
        /// a payload two conforming readers may read differently has to be
        /// refused rather than resolved. The Java and TypeScript readers
        /// reject it; this is the C# side of the same contract.
        ///
        /// <para>There is no duplicate-key switch on netstandard/net8
        /// <c>JsonDocument</c>, so the check is a token pass: a property
        /// name is exactly a name token, and the reader hands them back
        /// already unescaped, which is what makes <c>"sql"</c> and
        /// <c>"\u0073ql"</c> collide the way they should.</para>
        /// </summary>
        private static void RejectDuplicateKeys(string json)
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions());
            var stack = new Stack<HashSet<string>>();
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        stack.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        stack.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        string name = reader.GetString();
                        if (!stack.Peek().Add(name))
                        {
                            throw new InvalidDataException(
                                "duplicate object key \"" + name
                                + "\"; an object may not repeat a key (docs/script-envelope.md)");
                        }
                        break;
                }
            }
        }

        public static SqliteHostScript LoadPayload(string relativePath)
        {
            return Parse(File.ReadAllText(FixturePaths.Payload(relativePath)));
        }

        public static SqliteHostScript Parse(string json)
        {
            RejectDuplicateKeys(json);
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
                    return SqliteHostBindingValue.Blob(DecodeCanonicalBase64(element.GetProperty("value").GetString()));
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

        /// <summary>
        /// Decode a <c>blob</c> value, refusing every non-canonical
        /// spelling of the same bytes (docs/script-envelope.md).
        ///
        /// <para><see cref="Convert.FromBase64String"/> alone is too
        /// permissive on both counts the contract pins: it silently skips
        /// embedded ASCII whitespace, and it discards the trailing padding
        /// bits instead of requiring them to be zero — so <c>"QR=="</c>
        /// decoded to the same 0x41 as <c>"QQ=="</c> even though it is not
        /// the encoding of it. Java and TypeScript both refuse those with a
        /// shape regex; re-encoding and demanding the exact input back
        /// accepts the identical set, and is the rule
        /// <c>ScriptEnvelopeVerifier.TryDecodeBase64</c> already applies to
        /// the delivery signature for the same reason: an envelope is
        /// signed bytes, and several spellings of one blob force a reader
        /// to pick one to re-emit.</para>
        /// </summary>
        private static byte[] DecodeCanonicalBase64(string value)
        {
            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(value);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(
                    "blob value \"" + value + "\" is not canonical base64"
                    + " (standard alphabet, padded, no whitespace, padding bits zero).", ex);
            }
            if (Convert.ToBase64String(decoded) != value)
            {
                throw new InvalidDataException(
                    "blob value \"" + value + "\" is not canonical base64"
                    + " (standard alphabet, padded, no whitespace, padding bits zero).");
            }
            return decoded;
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

        /// <summary>
        /// An optional string field, absent ONLY by being missing from the
        /// object. An explicit JSON <c>null</c> is a type error
        /// (docs/script-envelope.md), and it is the one shape
        /// <see cref="JsonElement.GetString"/> does not catch by itself:
        /// every other wrong kind throws, but a <c>JsonValueKind.Null</c>
        /// hands back a C# <c>null</c> that is indistinguishable from the
        /// absent field. So <c>{"scriptId": null}</c> read as "no scriptId"
        /// here while the Java and TypeScript readers refused the payload —
        /// the same envelope publishable through one SDK and not another,
        /// which is exactly what the rule exists to prevent.
        /// </summary>
        private static string GetString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value))
            {
                return null;
            }
            RejectNull(value, property);
            return value.GetString();
        }

        private static List<string> GetStringList(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value))
            {
                return null;
            }
            RejectNull(value, property);
            var list = new List<string>();
            foreach (JsonElement entry in value.EnumerateArray())
            {
                // Same rule one level down: a null entry is not a string,
                // and `GetString` would silently make it one.
                RejectNull(entry, property + " entry");
                list.Add(entry.GetString());
            }
            return list;
        }

        private static void RejectNull(JsonElement value, string what)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidDataException(
                    what + " is null; an explicit JSON null is not an absent field"
                    + " (docs/script-envelope.md).");
            }
        }
    }
}
