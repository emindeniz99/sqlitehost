using System;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;
using ClassicGen = Example.Game.Generated;
using CompactGen = Example.Game.Generated.Compact;
using UltraGen = Example.Game.Generated.Ultra;

namespace SqliteHost.Tests
{
    /// <summary>
    /// A REAL column can hold ±Infinity: SQLite parses the literal 9e999
    /// into one, and `input_score REAL NOT NULL` accepts it. Reading that
    /// column back is not JSON, so the envelope's finite-only rule does not
    /// apply to it — the value is a legitimate double and must reach the
    /// handler unchanged. All three profiles lower to one execution core,
    /// so all three must agree (ErasedScalarFields: "the single home of the
    /// scalar column mapping rules ... so semantics cannot drift between
    /// profiles").
    /// </summary>
    public class NonFiniteRealTests
    {
        private const string InsertInfiniteScore =
            "INSERT INTO call_record_score (call_id, input_key, input_score)"
            + " VALUES ('c-1', 'k', 9e999)";

        private static SqliteHostScript Script()
        {
            return Scripts.New(Scripts.Step("only", Scripts.Statement(InsertInfiniteScore)));
        }

        [SkippableFact]
        public void Classic_InfiniteRealReachesTheHandler()
        {
            SampleHostFloor.SkipBelowFloor();
            using var factory = new TestWorkspaceFactory();
            var handlers = new ClassicScoreProbe();
            var runtime = new SqliteHostRuntime<ClassicGen.IGeneratedHostHandlers>(
                factory, ClassicGen.GeneratedHostDefinition.Build(), handlers, null);

            SqliteHostRunResult result = runtime.Run(Script());

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(double.PositiveInfinity, handlers.Seen);
        }

        [SkippableFact]
        public void Compact_InfiniteRealReachesTheHandler()
        {
            SampleHostFloor.SkipBelowFloor();
            using var factory = new TestWorkspaceFactory();
            var handlers = new CompactScoreProbe();
            var runtime = new SqliteHostRuntime<CompactGen.IGeneratedHostHandlers>(
                factory, CompactGen.GeneratedHostDefinition.Build(), handlers, null);

            SqliteHostRunResult result = runtime.Run(Script());

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(double.PositiveInfinity, handlers.Seen);
        }

        [SkippableFact]
        public void Ultra_InfiniteRealReachesTheHandler()
        {
            SampleHostFloor.SkipBelowFloor();
            using var factory = new TestWorkspaceFactory();
            var handlers = new UltraScoreProbe();
            var runtime = new SqliteHostRuntime<UltraGen.IGeneratedHostHandlers>(
                factory, UltraGen.GeneratedHostDefinition.Build(), handlers, null);

            SqliteHostRunResult result = runtime.Run(Script());

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(double.PositiveInfinity, handlers.Seen);
        }

        // Probes: only recordScore is reachable from the script above; the
        // rest of each interface would be a bug in the test if it ran.
        private sealed class ClassicScoreProbe : ClassicGen.IGeneratedHostHandlers
        {
            public double Seen { get; private set; }

            public ClassicGen.RecordScoreResult RecordScore(ClassicGen.RecordScoreInput input)
            {
                Seen = input.Score;
                return new ClassicGen.RecordScoreResult { Average = 0d };
            }

            public ClassicGen.GetValueResult GetValue(ClassicGen.GetValueInput input) => throw new NotSupportedException();
            public ClassicGen.SetValueResult SetValue(ClassicGen.SetValueInput input) => throw new NotSupportedException();
            public ClassicGen.GetValuesResult GetValues(ClassicGen.GetValuesInput input) => throw new NotSupportedException();
            public ClassicGen.PutBlobResult PutBlob(ClassicGen.PutBlobInput input) => throw new NotSupportedException();
        }

        private sealed class CompactScoreProbe : CompactGen.IGeneratedHostHandlers
        {
            public double Seen { get; private set; }

            public CompactGen.RecordScoreResult RecordScore(CompactGen.RecordScoreInput input)
            {
                Seen = input.Score;
                return new CompactGen.RecordScoreResult { Average = 0d };
            }

            public CompactGen.GetValueResult GetValue(CompactGen.GetValueInput input) => throw new NotSupportedException();
            public CompactGen.SetValueResult SetValue(CompactGen.SetValueInput input) => throw new NotSupportedException();
            public CompactGen.GetValuesResult GetValues(CompactGen.GetValuesInput input) => throw new NotSupportedException();
            public CompactGen.PutBlobResult PutBlob(CompactGen.PutBlobInput input) => throw new NotSupportedException();
        }

        private sealed class UltraScoreProbe : UltraGen.IGeneratedHostHandlers
        {
            public double Seen { get; private set; }

            public SqliteHostUltraResult RecordScore(SqliteHostUltraCall call)
            {
                Seen = call.GetFloat64("score");
                return new SqliteHostUltraResult().SetFloat64("average", 0d);
            }

            public SqliteHostUltraResult GetValue(SqliteHostUltraCall call) => throw new NotSupportedException();
            public SqliteHostUltraResult SetValue(SqliteHostUltraCall call) => throw new NotSupportedException();
            public SqliteHostUltraResult GetValues(SqliteHostUltraCall call) => throw new NotSupportedException();
            public SqliteHostUltraResult PutBlob(SqliteHostUltraCall call) => throw new NotSupportedException();
        }
    }
}
