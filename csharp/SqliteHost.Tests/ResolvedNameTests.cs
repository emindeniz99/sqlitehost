using System.Collections.Generic;
using System.Linq;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// The resolved-name side channel (Tables / Column / ChildTable). A
    /// generated host carries the physical names its own schema SQL was
    /// built from, so the runtime must read and write exactly those; a
    /// hand-written definition declares logical names only and the naming
    /// derivation stays the default. Both halves are asserted here because
    /// only the pair is the contract: an override that the runtime ignored
    /// and a derivation that an override silently replaced are the same
    /// bug seen from two sides.
    /// </summary>
    public class ResolvedNameTests
    {
        public class TagItem
        {
            public string Tag { get; set; }
        }

        public class LookupInput
        {
            public string Key { get; set; }
            public List<TagItem> Tags { get; set; } = new List<TagItem>();
        }

        public class LookupResult
        {
            public long Hits { get; set; }
            public List<TagItem> Echo { get; set; } = new List<TagItem>();
        }

        public interface ILookupHandlers
        {
            LookupResult Lookup(LookupInput input);
        }

        private sealed class EchoLookupHandlers : ILookupHandlers
        {
            public LookupInput LastInput { get; private set; }

            public LookupResult Lookup(LookupInput input)
            {
                LastInput = input;
                var result = new LookupResult { Hits = input.Tags.Count };
                foreach (TagItem tag in input.Tags)
                {
                    result.Echo.Add(new TagItem { Tag = tag.Tag + "!" });
                }
                return result;
            }
        }

        /// <summary>
        /// The method every test here registers. <paramref name="resolved"/>
        /// switches between the generated-host shape (physical names carried
        /// on the spec) and the hand-written shape (logical names only).
        /// </summary>
        private static IHostMethodSpec<ILookupHandlers> LookupSpec(bool resolved)
        {
            var builder = HostMethod
                .For<ILookupHandlers, LookupInput, LookupResult>("lookup")
                .ApiLevel(1);
            if (resolved)
            {
                builder = builder.Tables("legacy_calls", "legacy_results", "trg_legacy_calls_queue");
            }
            return builder
                .Inputs(i =>
                {
                    i.Text("key", (x, v) => x.Key = v);
                    if (resolved)
                    {
                        i.Column("legacy_key");
                    }
                    i.List<TagItem>("tags", (x, v) => x.Tags = v, item =>
                    {
                        item.Text("tag", (x, v) => x.Tag = v);
                        if (resolved)
                        {
                            item.Column("legacy_tag");
                        }
                    });
                    if (resolved)
                    {
                        i.ChildTable("legacy_calls_tags");
                    }
                })
                .Results(r =>
                {
                    r.Long("hits", x => x.Hits);
                    if (resolved)
                    {
                        r.Column("legacy_hits");
                    }
                    r.List<TagItem>("echo", x => x.Echo, item =>
                    {
                        item.Text("tag", x => x.Tag);
                        if (resolved)
                        {
                            item.Column("legacy_echo_tag");
                        }
                    });
                    if (resolved)
                    {
                        r.ChildTable("legacy_results_echo");
                    }
                })
                .Handler((handlers, input) => handlers.Lookup(input))
                .Build();
        }

        private static SqliteHostDefinition<ILookupHandlers> Definition(bool resolved)
        {
            return SqliteHostDefinition
                .ForHandlers<ILookupHandlers>()
                .Methods(new[] { LookupSpec(resolved) });
        }

        [Fact]
        public void ResolvedNames_ReplaceEveryDerivedTableColumnAndTrigger()
        {
            string script = Definition(resolved: true).GenerateSchemaScript();

            Assert.Contains("CREATE TABLE legacy_calls (", script);
            Assert.Contains("    legacy_key TEXT NOT NULL\n", script);
            Assert.Contains("CREATE TABLE legacy_calls_tags (", script);
            Assert.Contains("    legacy_tag TEXT NOT NULL,", script);
            Assert.Contains("CREATE TABLE legacy_results (", script);
            Assert.Contains("    legacy_hits INTEGER NOT NULL\n", script);
            Assert.Contains("CREATE TABLE legacy_results_echo (", script);
            Assert.Contains("    legacy_echo_tag TEXT NOT NULL,", script);
            Assert.Contains("CREATE TRIGGER trg_legacy_calls_queue\nAFTER INSERT ON legacy_calls\n", script);

            // Nothing derived survives: the schema and the runtime read the
            // same slot, so a leftover derived name here would be a table
            // the host writes to and never created.
            Assert.DoesNotContain("call_lookup", script);
            Assert.DoesNotContain("result_lookup", script);
            Assert.DoesNotContain("input_key", script);
            Assert.DoesNotContain("result_hits", script);
        }

        [Fact]
        public void WithoutResolvedNames_TheDerivationStaysTheDefault()
        {
            SqliteHostNaming naming = SqliteHostNaming.Default;
            string script = Definition(resolved: false).GenerateSchemaScript();

            Assert.Contains(
                "CREATE TABLE " + NamingDerivation.CallTable(naming, "lookup") + " (", script);
            Assert.Contains(
                "    " + NamingDerivation.InputColumn(naming, "key") + " TEXT NOT NULL\n", script);
            Assert.Contains(
                "CREATE TABLE " + NamingDerivation.InputListTable(naming, "lookup", "tags") + " (", script);
            Assert.Contains(
                "CREATE TABLE " + NamingDerivation.ResultTable(naming, "lookup") + " (", script);
            Assert.Contains(
                "    " + NamingDerivation.ResultColumn(naming, "hits") + " INTEGER NOT NULL\n", script);
            Assert.Contains(
                "CREATE TABLE " + NamingDerivation.ResultListTable(naming, "lookup", "echo") + " (", script);
            Assert.Contains(
                "CREATE TRIGGER " + NamingDerivation.QueueTrigger(naming, "lookup") + "\n", script);
        }

        [SkippableFact]
        public void ResolvedNames_AreTheTablesTheRuntimeActuallyReadsAndWrites()
        {
            SampleHostFloor.SkipBelowFloor();
            var handlers = new EchoLookupHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = new SqliteHostRuntime<ILookupHandlers>(
                connectionFactory: factory,
                hostDefinition: Definition(resolved: true),
                handlers: handlers,
                options: null);

            var script = Scripts.New(Scripts.Step("only",
                Scripts.Statement(
                    "INSERT INTO legacy_calls (call_id, legacy_key) VALUES (:callId, :key)",
                    ("callId", SqliteHostBindingValue.Text("c-1")),
                    ("key", SqliteHostBindingValue.Text("k1"))),
                Scripts.Statement(
                    "INSERT INTO legacy_calls_tags (call_id, item_index, legacy_tag)"
                    + " VALUES ('c-1', 0, 'red')"),
                Scripts.Statement(
                    "INSERT INTO legacy_calls_tags (call_id, item_index, legacy_tag)"
                    + " VALUES ('c-1', 1, 'blue')")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);

            // Read side: the handler saw the row and the child rows the
            // script wrote under the resolved names.
            Assert.Equal("k1", handlers.LastInput.Key);
            Assert.Equal(new[] { "red", "blue" }, handlers.LastInput.Tags.Select(t => t.Tag));

            // Write side: the result row and its child rows landed in the
            // resolved tables under the resolved columns.
            var hits = factory.LastWorkspace.Query(
                "SELECT status, legacy_hits FROM legacy_results WHERE call_id = 'c-1'",
                null,
                row => new { Status = row.GetText(0), Hits = row.GetInt64(1) });
            var hitRow = Assert.Single(hits);
            Assert.Equal("done", hitRow.Status);
            Assert.Equal(2L, hitRow.Hits);

            var echo = factory.LastWorkspace.Query(
                "SELECT legacy_echo_tag FROM legacy_results_echo WHERE call_id = 'c-1'"
                + " ORDER BY item_index",
                null,
                row => row.GetText(0));
            Assert.Equal(new[] { "red!", "blue!" }, echo);
        }

        [Fact]
        public void ASideChannelWithNothingDeclaredBeforeItFailsLoud()
        {
            // Silently dropping the name would leave the runtime deriving a
            // column the generated schema never created.
            var builder = HostMethod
                .For<ILookupHandlers, LookupInput, LookupResult>("lookup");
            Assert.Throws<System.InvalidOperationException>(
                () => builder.Inputs(i => i.Column("legacy_key")));
            Assert.Throws<System.InvalidOperationException>(
                () => builder.Inputs(i => i.ChildTable("legacy_calls_tags")));
        }
    }
}
