using System;
using System.Collections.Generic;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Build-time validation of derived table names (docs/naming.md,
    /// docs/validation.md §1): method or list field names that derive to
    /// the same physical table must fail loud at registration — mirroring
    /// the TypeSpec duplicate-table-name diagnostic — instead of building
    /// cleanly and failing the first Run with an opaque FailedSchema
    /// "table already exists" error.
    /// </summary>
    public class DerivedTableNameTests
    {
        private sealed class KeyInput
        {
            public string Key { get; set; }
        }

        private sealed class ValueResult
        {
            public long Value { get; set; }
        }

        private sealed class ListInput
        {
            public List<PairItem> Keys { get; set; }
        }

        private static IHostMethodSpec<object> ScalarSpec(string methodName)
        {
            return HostMethod
                .For<object, KeyInput, ValueResult>(methodName)
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((h, input) => new ValueResult())
                .Build();
        }

        [Fact]
        public void DistinctMethodNames_DerivingSameTables_ThrowAtBuildTime()
        {
            // Snake-case derivation aliases distinct logical names:
            // "getValue" and "get_value" both derive call_get_value. The
            // contract (docs/validation.md §1, TypeSpec duplicate-table-name)
            // is fail-loud at registration, not an opaque FailedSchema
            // "table already exists" on the first Run.
            var builder = SqliteHostDefinition.ForHandlers<object>();

            var ex = Assert.Throws<ArgumentException>(
                () => builder.Methods(new[] { ScalarSpec("getValue"), ScalarSpec("get_value") }));
            Assert.Contains("'call_get_value'", ex.Message);
            Assert.Contains("'getValue'", ex.Message);
            Assert.Contains("'get_value'", ex.Message);
        }

        [Fact]
        public void EqualCallAndResultPrefixes_FuseTables_ThrowAtBuildTime()
        {
            // Naming config must not silently fuse the call and result
            // table of one method — split call/result tables are the
            // splitResultTables protocol feature.
            var builder = SqliteHostDefinition
                .ForHandlers<object>()
                .Naming(n => n.CallTablePrefix("t_").ResultTablePrefix("t_"));

            var ex = Assert.Throws<ArgumentException>(
                () => builder.Methods(new[] { ScalarSpec("getValue") }));
            Assert.Contains("'t_get_value'", ex.Message);
            Assert.Contains("'getValue'", ex.Message);
        }

        [Fact]
        public void WorkspaceTableCollidingWithDerivedTable_CaseInsensitively_ThrowsAtBuildTime()
        {
            // SQLite resolves table names case-insensitively: a queue table
            // named "CALL_GET_VALUE" and the derived call_get_value are the
            // same table, so the definition must not build (mirrors the
            // TypeSpec validator's lowercased comparisons).
            var builder = SqliteHostDefinition
                .ForHandlers<object>()
                .Naming(n => n.QueueTable("CALL_GET_VALUE"));

            var ex = Assert.Throws<ArgumentException>(
                () => builder.Methods(new[] { ScalarSpec("getValue") }));
            Assert.Contains("'call_get_value'", ex.Message);
            Assert.Contains("getValue", ex.Message);
        }

        [Fact]
        public void MethodNameCollidingWithListChildTable_ThrowsAtBuildTime()
        {
            // List child tables share the table namespace: the call table
            // of method "getValues__inputKeys" derives to the same name as
            // the child table of list field "keys" of method "getValues"
            // (call_get_values__input_keys), mirroring TypeSpec's claiming
            // of child tables alongside call/result tables.
            var listSpec = HostMethod
                .For<object, ListInput, ValueResult>("getValues")
                .Inputs(i => i
                    .List<PairItem>("keys", (x, v) => x.Keys = v, item => item
                        .Text("key", (p, v) => p.K = v)))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((h, input) => new ValueResult())
                .Build();
            var builder = SqliteHostDefinition.ForHandlers<object>();

            var ex = Assert.Throws<ArgumentException>(
                () => builder.Methods(new[] { listSpec, ScalarSpec("getValues__inputKeys") }));
            Assert.Contains("'call_get_values__input_keys'", ex.Message);
            Assert.Contains("'getValues'", ex.Message);
            Assert.Contains("'getValues__inputKeys'", ex.Message);
        }
    }
}
