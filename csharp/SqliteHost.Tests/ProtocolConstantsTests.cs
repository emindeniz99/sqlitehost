using System;
using System.Collections.Generic;
using System.IO;
using SqliteHost.Tests.Fixtures;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Pins the protocol values the runtime consumes to the generated
    /// ProtocolConstants, and guards the hand-written runtime sources
    /// against restating them. A second copy of a protocol literal in C#
    /// is unreachable from the manifest, so it drifts silently the first
    /// time the contract moves (docs/proposals/rule-parameters-as-data.md).
    /// </summary>
    public class ProtocolConstantsTests
    {
        [Fact]
        public void FeaturesV1_IsWhatEveryDefinitionAdvertises()
        {
            var definition = SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new IHostMethodSpec<object>[0]);
            Assert.Equal(ProtocolConstants.FeaturesV1, definition.SupportedFeatures);
        }

        [Fact]
        public void DefaultMinSqliteVersionNumber_IsAppliedWhenTheBuilderStaysSilent()
        {
            var definition = SqliteHostDefinition
                .ForHandlers<object>()
                .Methods(new IHostMethodSpec<object>[0]);
            Assert.Equal(
                ProtocolConstants.DefaultMinSqliteVersionNumber,
                definition.MinSqliteVersionNumber);
        }

        [Fact]
        public void EngineV1_IsTheEngineTheRuntimeAccepts()
        {
            Assert.Equal("sqlite-host-v1", ProtocolConstants.EngineV1);
        }

        /// <summary>
        /// The literals below reach C# only through ProtocolConstants.g.cs.
        /// Any hand-written runtime source that spells one out again is a
        /// second source of truth for a manifest-owned value.
        /// </summary>
        [Theory]
        [InlineData("\"sqlite-host-v1\"")]
        [InlineData("\"typedNamedBindings\"")]
        [InlineData("\"splitResultTables\"")]
        [InlineData("3019003")]
        public void HandWrittenRuntimeSources_DoNotRestateProtocolLiterals(string literal)
        {
            var offenders = new List<string>();
            string runtimeDir = Path.Combine(
                Directory.GetParent(FixturePaths.Root).FullName, "csharp", "SqliteHost.Runtime");
            foreach (string file in Directory.GetFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly))
            {
                if (file.EndsWith(".g.cs", StringComparison.Ordinal))
                {
                    continue;
                }
                if (File.ReadAllText(file).Contains(literal))
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }
            Assert.True(offenders.Count == 0,
                "Protocol literal " + literal + " restated by hand in: " + string.Join(", ", offenders));
        }
    }
}
