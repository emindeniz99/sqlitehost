using System.Collections.Generic;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.Fixtures;
using SqliteHost.Tests.TestSupport;
using Xunit;
using ClassicGen = Example.Game.Generated;
using CompactGen = Example.Game.Generated.Compact;
using UltraGen = Example.Game.Generated.Ultra;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Proves the three generated-code profiles (classic, compact, ultra)
    /// are behaviorally interchangeable: identical DDL and, for every
    /// committed valid fixture, identical run results, identical handler
    /// side effects, and identical final result-table contents on a real
    /// adapter (Microsoft.Data.Sqlite in-memory). All profiles lower to
    /// the same erased execution core — these tests pin that equivalence
    /// at the wire level.
    /// </summary>
    public class ProfileEquivalenceTests
    {
        private sealed class ProfileRun
        {
            public SqliteHostRunResult Result;
            public Dictionary<string, long> Storage;
            public Dictionary<string, byte[]> Blobs;
            public List<string> Log;
            public List<string> ResultTableDump;
        }

        private static readonly string[] EquivalenceFixtures =
        {
            "valid/example-001-read-then-conditional-write.json",
            "valid/example-002-list-roundtrip.json",
            "valid/example-003-runtime-inputs.json",
            "valid/example-004-blob.json",
            "valid/example-006-floats.json",
            "valid/example-007-mixed-prefix.json",
            "valid/example-008-variables.json",
            "valid/example-009-halt.json",
            "valid/example-010-inline.json",
        };

        [Fact]
        public void AllThreeProfiles_GenerateIdenticalSchemaScripts()
        {
            string classic = ClassicGen.GeneratedHostDefinition.Build().GenerateSchemaScript();
            string compact = CompactGen.GeneratedHostDefinition.Build().GenerateSchemaScript();
            string ultra = UltraGen.GeneratedHostDefinition.Build().GenerateSchemaScript();

            Assert.Equal(classic, compact);
            Assert.Equal(classic, ultra);
            Assert.Equal(ClassicGen.GeneratedSchemaSql.SchemaScript, classic);
            Assert.Equal(CompactGen.GeneratedSchemaSql.SchemaScript, compact);
            Assert.Equal(UltraGen.GeneratedSchemaSql.SchemaScript, ultra);
        }

        [SkippableFact]
        public void EveryValidFixture_RunsIdenticallyOnAllProfiles()
        {
            SampleHostFloor.SkipBelowFloor();
            foreach (string fixture in EquivalenceFixtures)
            {
                ProfileRun classic = RunClassic(fixture);
                ProfileRun compact = RunCompact(fixture);
                ProfileRun ultra = RunUltra(fixture);

                AssertEquivalent(fixture, classic, compact, "compact");
                AssertEquivalent(fixture, classic, ultra, "ultra");
            }
        }

        private static void AssertEquivalent(string fixture, ProfileRun expected, ProfileRun actual, string profile)
        {
            string context = fixture + " [" + profile + "]";
            Assert.True(expected.Result.Status == actual.Result.Status,
                context + ": status " + actual.Result.Status + " != " + expected.Result.Status);
            Assert.True(expected.Result.ErrorCode == actual.Result.ErrorCode,
                context + ": errorCode " + (actual.Result.ErrorCode ?? "<null>")
                + " != " + (expected.Result.ErrorCode ?? "<null>"));
            Assert.Equal(expected.Result.ExecutedCallCount, actual.Result.ExecutedCallCount);
            Assert.Equal(expected.Result.InlineCallCount, actual.Result.InlineCallCount);
            Assert.Equal(expected.Result.Halted, actual.Result.Halted);
            Assert.Equal(expected.Result.HaltMessage, actual.Result.HaltMessage);
            Assert.Equal(expected.Storage, actual.Storage);
            Assert.Equal(expected.Blobs.Keys, actual.Blobs.Keys);
            foreach (KeyValuePair<string, byte[]> blob in expected.Blobs)
            {
                Assert.Equal(blob.Value, actual.Blobs[blob.Key]);
            }
            Assert.Equal(expected.Log, actual.Log);
            Assert.Equal(expected.ResultTableDump, actual.ResultTableDump);
        }

        private static ProfileRun RunClassic(string fixture)
        {
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var handlers = SeedClassic();
            var runtime = new SqliteHostRuntime<ClassicGen.IGeneratedHostHandlers>(
                factory, ClassicGen.GeneratedHostDefinition.Build(), handlers, null);
            SqliteHostRunResult result = runtime.Run(ScriptEnvelopeJson.LoadPayload(fixture));
            return new ProfileRun
            {
                Result = result,
                Storage = handlers.Storage,
                Blobs = handlers.Blobs,
                Log = handlers.Log,
                ResultTableDump = DumpResultTables(factory),
            };
        }

        private static ProfileRun RunCompact(string fixture)
        {
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var handlers = new CompactFakeGameHandlers();
            SeedShared(handlers.Storage);
            var runtime = new SqliteHostRuntime<CompactGen.IGeneratedHostHandlers>(
                factory, CompactGen.GeneratedHostDefinition.Build(), handlers, null);
            SqliteHostRunResult result = runtime.Run(ScriptEnvelopeJson.LoadPayload(fixture));
            return new ProfileRun
            {
                Result = result,
                Storage = handlers.Storage,
                Blobs = handlers.Blobs,
                Log = handlers.Log,
                ResultTableDump = DumpResultTables(factory),
            };
        }

        private static ProfileRun RunUltra(string fixture)
        {
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var handlers = new UltraFakeGameHandlers();
            SeedShared(handlers.Storage);
            var runtime = new SqliteHostRuntime<UltraGen.IGeneratedHostHandlers>(
                factory, UltraGen.GeneratedHostDefinition.Build(), handlers, null);
            SqliteHostRunResult result = runtime.Run(ScriptEnvelopeJson.LoadPayload(fixture));
            return new ProfileRun
            {
                Result = result,
                Storage = handlers.Storage,
                Blobs = handlers.Blobs,
                Log = handlers.Log,
                ResultTableDump = DumpResultTables(factory),
            };
        }

        private static FakeGameHandlers SeedClassic()
        {
            var handlers = new FakeGameHandlers();
            SeedShared(handlers.Storage);
            return handlers;
        }

        /// <summary>Shared pre-state so conditional fixtures take the same branch everywhere.</summary>
        private static void SeedShared(Dictionary<string, long> storage)
        {
            storage["example-key"] = 7;
            storage["alpha"] = 10;
            storage["gamma"] = 30;
        }

        /// <summary>
        /// Serializes the final contents of every result_ parent and child
        /// table (ordered) so the wire-level output of a run can be compared
        /// across profiles.
        /// </summary>
        private static List<string> DumpResultTables(TestWorkspaceFactory factory)
        {
            var dump = new List<string>();
            if (factory.LastWorkspace == null)
            {
                return dump;
            }
            IReadOnlyList<string> tables = factory.LastWorkspace.Query(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'result_%' ORDER BY name",
                new List<SqliteHostBinding>(),
                row => row.GetText(0));
            foreach (string table in tables)
            {
                IReadOnlyList<string> rows = factory.LastWorkspace.Query(
                    "SELECT quote(call_id), " + DumpColumnsExpression(factory, table)
                    + " FROM " + table + " ORDER BY rowid",
                    new List<SqliteHostBinding>(),
                    row => row.GetText(0) + "|" + row.GetText(1));
                foreach (string row in rows)
                {
                    dump.Add(table + "|" + row);
                }
            }
            return dump;
        }

        /// <summary>One quoted-and-concatenated expression over all non-call_id columns of the table.</summary>
        private static string DumpColumnsExpression(TestWorkspaceFactory factory, string table)
        {
            IReadOnlyList<string> columns = factory.LastWorkspace.Query(
                "SELECT name FROM pragma_table_info('" + table + "') WHERE name <> 'call_id' ORDER BY cid",
                new List<SqliteHostBinding>(),
                row => row.GetText(0));
            var parts = new List<string>(columns.Count);
            foreach (string column in columns)
            {
                parts.Add("quote(" + column + ")");
            }
            return parts.Count == 0 ? "''" : string.Join(" || ',' || ", parts);
        }
    }
}
