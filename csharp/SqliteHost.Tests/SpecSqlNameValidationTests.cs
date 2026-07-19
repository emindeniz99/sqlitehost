using System;
using System.Collections.Generic;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Build-time validation of field SQL names (docs/naming.md,
    /// docs/validation.md §1): duplicate SQL names within one input/result
    /// shape or one list item shape must fail loud at registration —
    /// mirroring the TypeSpec duplicate-sql-name diagnostic — instead of
    /// registering cleanly and failing every Run with FailedSchema
    /// ("duplicate column name"), far from the code that authored the
    /// spec.
    /// </summary>
    public class SpecSqlNameValidationTests
    {
        private sealed class KeyInput
        {
            public string Key { get; set; }
            public string Other { get; set; }
            public List<PairItem> Pairs { get; set; }
        }

        private sealed class ValueResult
        {
            public long Value { get; set; }
            public long Other { get; set; }
            public List<PairItem> Pairs { get; set; }
        }

        private static SqliteHostDefinition<object> Define(params IHostMethodSpec<object>[] specs)
        {
            return SqliteHostDefinition.ForHandlers<object>().Methods(specs);
        }

        [Fact]
        public void DuplicateInputSqlName_ThrowsAtBuildTime()
        {
            // Without this check the invalid definition registers cleanly
            // and every Run fails FailedSchema far from the code that
            // authored it.
            var spec = HostMethod
                .For<object, KeyInput, ValueResult>("getValue")
                .Inputs(i => i
                    .Text("key", (x, v) => x.Key = v)
                    .Text("key", (x, v) => x.Other = v))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((h, input) => new ValueResult())
                .Build();

            var ex = Assert.Throws<ArgumentException>(() => Define(spec));
            Assert.Contains("'key'", ex.Message);
            Assert.Contains("occurs more than once", ex.Message);
            Assert.Contains("getValue", ex.Message);
        }

        [Fact]
        public void DuplicateResultSqlName_ThrowsAtBuildTime()
        {
            var spec = HostMethod
                .For<object, KeyInput, ValueResult>("getValue")
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r
                    .Long("value", x => x.Value)
                    .Long("value", x => x.Other))
                .Handler((h, input) => new ValueResult())
                .Build();

            var ex = Assert.Throws<ArgumentException>(() => Define(spec));
            Assert.Contains("'value'", ex.Message);
            Assert.Contains("occurs more than once", ex.Message);
            Assert.Contains("result shape", ex.Message);
        }

        [Fact]
        public void DuplicateSqlNameWithinListItemShape_ThrowsAtBuildTime()
        {
            var spec = HostMethod
                .For<object, KeyInput, ValueResult>("getValue")
                .Inputs(i => i
                    .List<PairItem>("pairs", (x, v) => x.Pairs = v, item => item
                        .Text("k", (p, v) => p.K = v)
                        .Text("k", (p, v) => p.K = v)))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((h, input) => new ValueResult())
                .Build();

            var ex = Assert.Throws<ArgumentException>(() => Define(spec));
            Assert.Contains("'k'", ex.Message);
            Assert.Contains("item shape of list field 'pairs'", ex.Message);
        }

        [Fact]
        public void ScalarAndListSharingSqlNameInOneShape_ThrowsAtBuildTime()
        {
            // Scalar and list fields share the shape namespace exactly as
            // TypeSpec's duplicate-sql-name treats model properties — a
            // list's name is claimed even though it materializes as a
            // child table.
            var spec = HostMethod
                .For<object, KeyInput, ValueResult>("getValue")
                .Inputs(i => i
                    .Text("pairs", (x, v) => x.Key = v)
                    .List<PairItem>("pairs", (x, v) => x.Pairs = v, item => item
                        .Text("k", (p, v) => p.K = v)))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((h, input) => new ValueResult())
                .Build();

            var ex = Assert.Throws<ArgumentException>(() => Define(spec));
            Assert.Contains("'pairs'", ex.Message);
            Assert.Contains("occurs more than once", ex.Message);
        }

        [Fact]
        public void EmptyInputListItemShape_ThrowsAtBuildTime()
        {
            // Mirrors TypeSpec's empty-list-item diagnostic: a zero-field
            // item shape would make the runtime build "SELECT  FROM child"
            // for every queued call — a guaranteed FailedSql far from the
            // code that authored the spec.
            var spec = HostMethod
                .For<object, KeyInput, ValueResult>("getValue")
                .Inputs(i => i
                    .Text("key", (x, v) => x.Key = v)
                    .List<PairItem>("pairs", (x, v) => x.Pairs = v, item => { }))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((h, input) => new ValueResult())
                .Build();

            var ex = Assert.Throws<ArgumentException>(() => Define(spec));
            Assert.Contains("'pairs'", ex.Message);
            Assert.Contains("at least one item field", ex.Message);
        }

        [Fact]
        public void EmptyResultListItemShape_ThrowsAtBuildTime()
        {
            var spec = HostMethod
                .For<object, KeyInput, ValueResult>("getValue")
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r
                    .Long("value", x => x.Value)
                    .List<PairItem>("pairs", x => x.Pairs, item => { }))
                .Handler((h, input) => new ValueResult())
                .Build();

            var ex = Assert.Throws<ArgumentException>(() => Define(spec));
            Assert.Contains("'pairs'", ex.Message);
            Assert.Contains("at least one item field", ex.Message);
        }

        [Fact]
        public void SameSqlNameOnInputAndResultSides_IsAllowed()
        {
            // Different tables: the rule is per-shape, not global (TypeSpec
            // uses one fresh set per model) — guards against
            // over-tightening.
            var spec = HostMethod
                .For<object, KeyInput, ValueResult>("getValue")
                .Inputs(i => i.Text("value", (x, v) => x.Key = v))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((h, input) => new ValueResult())
                .Build();

            var definition = Define(spec);
            Assert.NotEmpty(definition.GenerateSchemaStatements());
        }

        [Fact]
        public void SameSqlNameAcrossMethods_IsAllowed()
        {
            IHostMethodSpec<object> Spec(string methodName)
            {
                return HostMethod
                    .For<object, KeyInput, ValueResult>(methodName)
                    .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                    .Results(r => r.Long("value", x => x.Value))
                    .Handler((h, input) => new ValueResult())
                    .Build();
            }

            var definition = Define(Spec("getValue"), Spec("getOther"));
            Assert.NotEmpty(definition.GenerateSchemaStatements());
        }

        [Fact]
        public void Ultra_DuplicateInputSqlName_ThrowsAtBuildTime()
        {
            // All three registration surfaces lower to the same erased
            // core, so the check must hold surface-independently, per
            // ErasedHostMethodSpec's identical-by-construction contract.
            var spec = UltraHostMethod
                .For<object>("getValue")
                .InputText("key")
                .InputText("key")
                .ResultLong("value")
                .Handler((h, call) => new SqliteHostUltraResult().SetInt64("value", 1))
                .Build();

            var ex = Assert.Throws<ArgumentException>(() => Define(spec));
            Assert.Contains("'key'", ex.Message);
            Assert.Contains("occurs more than once", ex.Message);
        }
    }
}
