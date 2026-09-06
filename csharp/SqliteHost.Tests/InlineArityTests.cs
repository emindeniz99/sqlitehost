using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SqliteHost.Tests.Fixtures;
using Xunit;
using ClassicGen = Example.Game.Generated;
using CompactGen = Example.Game.Generated.Compact;
using UltraGen = Example.Game.Generated.Ultra;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Inline arity has one source — the manifest, where
    /// codegen/core/src/frontend.ts derived it once from the declared input
    /// fields. Generated C# now carries it into .Inline(name, min, max),
    /// and InlineShapeRules only derives it for hand-written definitions,
    /// which have no manifest to read. Two rules that agree today can part
    /// ways silently, so these tests hold them against each other and
    /// against the manifest for every inline method of the sample host.
    /// </summary>
    public class InlineArityTests
    {
        [Fact]
        public void GeneratedSpecs_CarryTheManifestArity_InEveryProfile()
        {
            Dictionary<string, (int Min, int Max)> manifest = ManifestInlineArity();
            Assert.NotEmpty(manifest);

            AssertProfileMatchesManifest("classic", ClassicGen.GeneratedHostDefinition.Build().Methods, manifest);
            AssertProfileMatchesManifest("compact", CompactGen.GeneratedHostDefinition.Build().Methods, manifest);
            AssertProfileMatchesManifest("ultra", UltraGen.GeneratedHostDefinition.Build().Methods, manifest);
        }

        [Fact]
        public void EmittedArity_EqualsTheArityTheRuntimeWouldDerive()
        {
            // The fallback rule must still produce the emitted numbers. If
            // frontend.ts and InlineShapeRules ever disagree, this is where
            // it shows up rather than in a consumer's SQL.
            foreach (object method in ClassicGen.GeneratedHostDefinition.Build().Methods)
            {
                ErasedHostMethodSpec spec = ((ErasedSpecCarrier)method).Spec;
                if (spec.InlineFunction == null)
                {
                    continue;
                }
                (int min, int max) = DeriveArity(spec.SchemaModel.InputFields);
                Assert.Equal(min, spec.InlineFunction.MinArgs);
                Assert.Equal(max, spec.InlineFunction.MaxArgs);
            }
        }

        [Fact]
        public void HandWrittenSpec_WithoutADeclaredArity_StillDerivesIt()
        {
            var spec = HostMethod
                .For<object, ArityInput, ArityResult>("probe")
                .Inputs(i => i
                    .Text("a", (x, v) => x.A = v)
                    .OptionalLong("b", (x, v) => x.B = v))
                .Results(r => r.Long("value", x => x.Value))
                .Inline("fn_probe")
                .Handler((h, input) => new ArityResult())
                .Build();

            InlineFunctionModel inline = ((ErasedSpecCarrier)spec).Spec.InlineFunction;
            Assert.Equal(1, inline.MinArgs);
            Assert.Equal(2, inline.MaxArgs);
        }

        private static void AssertProfileMatchesManifest(
            string profile,
            IEnumerable<object> methods,
            Dictionary<string, (int Min, int Max)> manifest)
        {
            var seen = new List<string>();
            foreach (object method in methods)
            {
                ErasedHostMethodSpec spec = ((ErasedSpecCarrier)method).Spec;
                if (spec.InlineFunction == null)
                {
                    Assert.False(manifest.ContainsKey(spec.MethodName),
                        profile + ": '" + spec.MethodName + "' is inline in the manifest but not in the generated spec.");
                    continue;
                }
                seen.Add(spec.MethodName);
                (int min, int max) = manifest[spec.MethodName];
                Assert.Equal(min, spec.InlineFunction.MinArgs);
                Assert.Equal(max, spec.InlineFunction.MaxArgs);
            }
            Assert.Equal(manifest.Count, seen.Count);
        }

        private static (int Min, int Max) DeriveArity(IReadOnlyList<SchemaFieldModel> inputFields)
        {
            int required = 0;
            foreach (SchemaFieldModel field in inputFields)
            {
                if (!field.Optional)
                {
                    required++;
                }
            }
            return (required, inputFields.Count);
        }

        private static Dictionary<string, (int Min, int Max)> ManifestInlineArity()
        {
            string path = Path.Combine(FixturePaths.Root, "manifests", "sample-host.manifest.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var arity = new Dictionary<string, (int, int)>();
            foreach (JsonElement method in document.RootElement.GetProperty("methods").EnumerateArray())
            {
                if (!method.TryGetProperty("inline", out JsonElement inline)
                    || inline.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }
                arity[method.GetProperty("methodName").GetString()] =
                    (inline.GetProperty("minArgs").GetInt32(), inline.GetProperty("maxArgs").GetInt32());
            }
            return arity;
        }

        private sealed class ArityInput
        {
            public string A { get; set; }
            public long? B { get; set; }
        }

        private sealed class ArityResult
        {
            public long Value { get; set; }
        }
    }
}
