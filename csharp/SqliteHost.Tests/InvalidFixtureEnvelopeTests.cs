using System;
using System.Collections.Generic;
using System.IO;
using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.Fixtures;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// The <c>invalid/</c> half of the payload corpus, driven from the
    /// directory rather than from a list somebody remembered to extend.
    ///
    /// <para>Exactly one invalid fixture used to reach C# at all
    /// (duplicate-input-name). The other 60 were validated by Java and
    /// TypeScript only, so the runtime's own envelope precheck — the check
    /// that runs on the device, with no validator anywhere near it — had no
    /// corpus behind it. This suite runs every fixture whose fault the
    /// envelope layer is supposed to catch and asserts both the code AND
    /// that no workspace was opened; <see cref="FixtureCoverage"/> carries
    /// the reason for each fixture it does not run.</para>
    /// </summary>
    public class InvalidFixtureEnvelopeTests
    {
        public static TheoryData<string> EnvelopeRefusals()
        {
            var data = new TheoryData<string>();
            foreach (KeyValuePair<string, string> entry in FixtureCoverage.EnvelopeRefusals)
            {
                data.Add(entry.Key);
            }
            return data;
        }

        public static TheoryData<string> ReaderRefusals()
        {
            var data = new TheoryData<string>();
            foreach (KeyValuePair<string, string> entry in FixtureCoverage.ReaderRefusals)
            {
                data.Add(entry.Key);
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(EnvelopeRefusals))]
        public void EnvelopeFault_IsRefusedBeforeTheWorkspaceOpens(string fixtureName)
        {
            string expectedCode = FixtureCoverage.EnvelopeRefusals[fixtureName];
            var factory = new TestWorkspaceFactory();
            var handlers = new FakeGameHandlers();
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory,
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: null);

            SqliteHostRunResult result = runtime.Run(
                ScriptEnvelopeJson.LoadPayload("invalid/" + fixtureName));

            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.NotEqual(SqliteHostRunStatus.Completed, result.Status);
            // The whole point of a precheck: no engine is consulted, so the
            // refusal is identical on every adapter and every SQLite build,
            // which is why this suite is not parameterized across adapters.
            Assert.Equal(0, factory.OpenCount);
            Assert.Empty(handlers.Log);
        }

        [Theory]
        [MemberData(nameof(ReaderRefusals))]
        public void ReaderFault_IsRefusedWhileParsing(string fixtureName)
        {
            string expectedFragment = FixtureCoverage.ReaderRefusals[fixtureName];

            var ex = Assert.ThrowsAny<Exception>(
                () => ScriptEnvelopeJson.LoadPayload("invalid/" + fixtureName));

            Assert.Contains(expectedFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EveryInvalidFixture_IsEitherRunOrExplained()
        {
            var accounted = new HashSet<string>(StringComparer.Ordinal);
            foreach (IReadOnlyDictionary<string, string> table in new[]
            {
                FixtureCoverage.EnvelopeRefusals,
                FixtureCoverage.ReaderRefusals,
                FixtureCoverage.NotEnvelopeFaults,
            })
            {
                foreach (KeyValuePair<string, string> entry in table)
                {
                    Assert.True(accounted.Add(entry.Key),
                        entry.Key + " appears in two FixtureCoverage tables");
                    Assert.False(string.IsNullOrWhiteSpace(entry.Value),
                        entry.Key + " has a blank reason/code");
                }
            }

            var listed = new HashSet<string>(FixtureCoverage.ListPayloads("invalid"), StringComparer.Ordinal);
            var unaccounted = new List<string>(listed);
            unaccounted.RemoveAll(accounted.Contains);
            Assert.True(unaccounted.Count == 0,
                "new invalid fixture(s) with no C# decision: " + string.Join(", ", unaccounted)
                + " — add each to FixtureCoverage.EnvelopeRefusals with its code, or to "
                + "NotEnvelopeFaults with the reason the envelope layer cannot see the fault");

            var stale = new List<string>(accounted);
            stale.RemoveAll(listed.Contains);
            Assert.True(stale.Count == 0,
                "FixtureCoverage names fixture(s) that no longer exist: " + string.Join(", ", stale));
        }

        [Fact]
        public void EveryValidFixture_IsEitherRunOrExplained()
        {
            var listed = new HashSet<string>(FixtureCoverage.ListPayloads("valid"), StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in FixtureCoverage.UnrunValid)
            {
                Assert.True(listed.Contains(entry.Key),
                    "FixtureCoverage.UnrunValid names " + entry.Key + ", which does not exist");
                Assert.False(string.IsNullOrWhiteSpace(entry.Value),
                    entry.Key + " is skipped with a blank reason");
            }
            // The listing itself is the test data for
            // IntegrationFixtureTestsBase.EveryValidFixture_RunsToCompletion;
            // an empty directory would make that suite vacuously green.
            Assert.True(listed.Count > FixtureCoverage.UnrunValid.Count,
                "no valid fixture is executed by the runtime");
        }
    }
}
