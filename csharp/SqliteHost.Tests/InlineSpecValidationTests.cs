using System;
using System.Collections.Generic;
using Example.Game.Generated;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Build-time validation of the inline scalar-function surface
    /// (docs/proposals/inline-host-functions.md, docs/naming.md): the spec
    /// builder re-checks the eligibility shape rules fail-loud, and the
    /// definition validates the FunctionPrefix and function-name
    /// collisions.
    /// </summary>
    public class InlineSpecValidationTests
    {
        private sealed class KeyInput
        {
            public string Key { get; set; }
            public long? Fallback { get; set; }
            public long Count { get; set; }
        }

        private sealed class ListInput
        {
            public string Key { get; set; }
            public List<ItemDto> Items { get; set; }
        }

        private sealed class ItemDto
        {
            public string K { get; set; }
        }

        private sealed class OneResult
        {
            public long Value { get; set; }
        }

        private sealed class TwoResult
        {
            public long A { get; set; }
            public long B { get; set; }
        }

        private sealed class ListResult
        {
            public List<ItemDto> Entries { get; set; }
        }

        // ---- spec builder shape rules -------------------------------------

        [Fact]
        public void GeneratedGetValueSpec_CarriesInlineMetadata_MutatingSpecsDoNot()
        {
            var definition = GeneratedHostDefinition.Build();
            ErasedHostMethodSpec getValue = ((ErasedSpecCarrier)definition.Methods[0]).Spec;
            Assert.Equal("getValue", getValue.MethodName);
            Assert.NotNull(getValue.InlineFunction);
            Assert.Equal("fn_get_value", getValue.InlineFunction.FunctionName);
            Assert.Equal(1, getValue.InlineFunction.MinArgs);
            Assert.Equal(1, getValue.InlineFunction.MaxArgs);

            ErasedHostMethodSpec setValue = ((ErasedSpecCarrier)definition.Methods[1]).Spec;
            Assert.Equal("setValue", setValue.MethodName);
            Assert.Null(setValue.InlineFunction);
        }

        [Fact]
        public void Inline_WithEmptyFunctionName_Throws()
        {
            var builder = HostMethod.For<object, KeyInput, OneResult>("getValue");
            Assert.Throws<ArgumentException>(() => builder.Inline(""));
            Assert.Throws<ArgumentException>(() => builder.Inline(null));
        }

        [Fact]
        public void Inline_WithInputListField_FailsLoudAtBuild()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => HostMethod
                .For<object, ListInput, OneResult>("getValues")
                .Inputs(i => i
                    .Text("key", (x, v) => x.Key = v)
                    .List<ItemDto>("items", (x, v) => x.Items = v, item => item
                        .Text("k", (p, v) => p.K = v)))
                .Results(r => r.Long("value", x => x.Value))
                .Inline("fn_get_values")
                .Handler((h, input) => new OneResult())
                .Build());
            Assert.Contains("scalar fields only", ex.Message);
        }

        [Fact]
        public void Inline_WithResultListField_FailsLoudAtBuild()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => HostMethod
                .For<object, KeyInput, ListResult>("getValues")
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r.List<ItemDto>("entries", x => x.Entries, item => item
                    .Text("k", p => p.K)))
                .Inline("fn_get_values")
                .Handler((h, input) => new ListResult())
                .Build());
            Assert.Contains("scalar fields only", ex.Message);
        }

        [Fact]
        public void Inline_WithTwoResultFields_FailsLoudAtBuild()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => HostMethod
                .For<object, KeyInput, TwoResult>("getPair")
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r
                    .Long("a", x => x.A)
                    .Long("b", x => x.B))
                .Inline("fn_get_pair")
                .Handler((h, input) => new TwoResult())
                .Build());
            Assert.Contains("exactly one scalar field (found 2)", ex.Message);
        }

        [Fact]
        public void Inline_WithZeroResultFields_FailsLoudAtBuild()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => HostMethod
                .For<object, KeyInput, OneResult>("ping")
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Inline("fn_ping")
                .Handler((h, input) => new OneResult())
                .Build());
            Assert.Contains("exactly one scalar field (found 0)", ex.Message);
        }

        [Fact]
        public void Inline_RequiredFieldAfterOptional_FailsLoudAtBuild()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => HostMethod
                .For<object, KeyInput, OneResult>("pick")
                .Inputs(i => i
                    .Text("key", (x, v) => x.Key = v)
                    .OptionalLong("fallback", (x, v) => x.Fallback = v)
                    .Long("count", (x, v) => x.Count = v))
                .Results(r => r.Long("value", x => x.Value))
                .Inline("fn_pick")
                .Handler((h, input) => new OneResult())
                .Build());
            Assert.Contains("'count'", ex.Message);
            Assert.Contains("optional fields must be trailing", ex.Message);
        }

        [Fact]
        public void Inline_OptionalTrailingFields_ComputeTheArityRange()
        {
            ErasedHostMethodSpec spec = ((ErasedSpecCarrier)HostMethod
                .For<object, KeyInput, OneResult>("pick")
                .Inputs(i => i
                    .Text("key", (x, v) => x.Key = v)
                    .Long("count", (x, v) => x.Count = v)
                    .OptionalLong("fallback", (x, v) => x.Fallback = v))
                .Results(r => r.Long("value", x => x.Value))
                .Inline("fn_pick")
                .Handler((h, input) => new OneResult())
                .Build()).Spec;
            Assert.Equal(2, spec.InlineFunction.MinArgs);
            Assert.Equal(3, spec.InlineFunction.MaxArgs);
        }

        [Fact]
        public void NonInlineSpec_MayViolateEveryShapeRule_UnchangedBackCompat()
        {
            // The exact spec shapes that are inline-ineligible stay valid
            // when not inline-exposed.
            var spec = HostMethod
                .For<object, KeyInput, TwoResult>("getPair")
                .Inputs(i => i
                    .OptionalLong("fallback", (x, v) => x.Fallback = v)
                    .Long("count", (x, v) => x.Count = v))
                .Results(r => r
                    .Long("a", x => x.A)
                    .Long("b", x => x.B))
                .Handler((h, input) => new TwoResult())
                .Build();
            Assert.Equal("getPair", spec.MethodName);
            Assert.Null(((ErasedSpecCarrier)spec).Spec.InlineFunction);
        }

        // ---- definition-level naming validation ---------------------------

        private static IHostMethodSpec<object> InlineGetValueSpec(string functionName)
        {
            return HostMethod
                .For<object, KeyInput, OneResult>("getValue")
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r.Long("value", x => x.Value))
                .Inline(functionName)
                .Handler((h, input) => new OneResult())
                .Build();
        }

        [Fact]
        public void DefaultNaming_HasPinnedFunctionPrefix()
        {
            Assert.Equal("fn_", SqliteHostNaming.Default.FunctionPrefix);
        }

        [Fact]
        public void EmptyFunctionPrefix_FailsLoudAtDefinitionBuild()
        {
            var ex = Assert.Throws<ArgumentException>(() => SqliteHostDefinition
                .ForHandlers<object>()
                .Naming(n => n.FunctionPrefix(""))
                .Methods(new[] { InlineGetValueSpec("fn_get_value") }));
            Assert.Contains("FunctionPrefix", ex.Message);
        }

        [Fact]
        public void InlineFunctionName_CollidingWithADerivedTableName_FailsLoud()
        {
            var ex = Assert.Throws<ArgumentException>(() => SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new[] { InlineGetValueSpec("call_get_value") }));
            Assert.Contains("call_get_value", ex.Message);
            Assert.Contains("collides", ex.Message);
        }

        [Fact]
        public void InlineFunctionName_CollidingWithAWorkspaceTableName_FailsLoud()
        {
            var ex = Assert.Throws<ArgumentException>(() => SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new[] { InlineGetValueSpec("pending_host_calls") }));
            Assert.Contains("pending_host_calls", ex.Message);
        }

        [Fact]
        public void InlineFunctionName_CollidingWithASqliteBuiltin_FailsLoud()
        {
            // Registering an application-defined function replaces the
            // SQLite built-in with the same name and arity on the
            // workspace connection, so scripts calling max(...) would
            // silently run host code. Fail loud at definition build time,
            // mirroring the TypeSpec builtin-function-collision diagnostic
            // (codegen/core/src/validate.ts); the script lint deliberately
            // leaves un-prefixed built-in calls unlinted as "SQLite's
            // business" and relies on this guard.
            var ex = Assert.Throws<ArgumentException>(() => SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new[] { InlineGetValueSpec("max") }));
            Assert.Contains("max", ex.Message);
            Assert.Contains("built-in", ex.Message);
        }

        [Fact]
        public void InlineFunctionName_CollidingWithASqliteBuiltin_IsCaseInsensitive()
        {
            // SQLite resolves function names case-insensitively, so "MAX"
            // shadows max() just the same.
            var ex = Assert.Throws<ArgumentException>(() => SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new[] { InlineGetValueSpec("MAX") }));
            Assert.Contains("built-in", ex.Message);
        }

        [Fact]
        public void InlineFunctionName_ContainingABuiltinName_BuildsFine()
        {
            // The guard is exact-name membership, not substring: the fn_
            // prefix keeps its collision-free guarantee.
            var definition = SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new[] { InlineGetValueSpec("fn_max") });
            Assert.NotNull(definition);
        }

        [Fact]
        public void DuplicateInlineFunctionNames_AcrossMethods_FailLoud()
        {
            var other = HostMethod
                .For<object, KeyInput, OneResult>("getOther")
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r.Long("value", x => x.Value))
                .Inline("fn_get_value")
                .Handler((h, input) => new OneResult())
                .Build();
            var ex = Assert.Throws<ArgumentException>(() => SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new[] { InlineGetValueSpec("fn_get_value"), other }));
            Assert.Contains("more than one method", ex.Message);
        }

        [Fact]
        public void DefinitionSupportedFeatures_StayTheBaseFive_EvenWithInlineMethods()
        {
            // The inlineFunctions feature is factory-conditional: it is
            // computed by the runtime, never advertised by the definition.
            var definition = SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new[] { InlineGetValueSpec("fn_get_value") });
            Assert.Equal(
                new[] { "typedNamedBindings", "splitResultTables", "scriptInputs", "scriptVars", "scriptControl" },
                definition.SupportedFeatures);
        }

        // ---- SqliteHostScalarFunction shape ------------------------------

        [Fact]
        public void ScalarFunction_CtorValidatesNameRangeAndInvoke()
        {
            Func<SqliteHostBindingValue[], SqliteHostBindingValue> invoke =
                args => SqliteHostBindingValue.Null();
            Assert.Throws<ArgumentException>(
                () => new SqliteHostScalarFunction("", 0, 0, invoke));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SqliteHostScalarFunction("fn_x", -1, 0, invoke));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SqliteHostScalarFunction("fn_x", 2, 1, invoke));
            Assert.Throws<ArgumentNullException>(
                () => new SqliteHostScalarFunction("fn_x", 0, 0, null));

            var function = new SqliteHostScalarFunction("fn_x", 1, 3, invoke);
            Assert.Equal("fn_x", function.Name);
            Assert.Equal(1, function.MinArgs);
            Assert.Equal(3, function.MaxArgs);
            Assert.Same(invoke, function.Invoke);
        }
    }
}
