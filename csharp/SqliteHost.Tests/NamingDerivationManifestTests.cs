using System.IO;
using System.Text.Json;
using SqliteHost.Tests.Fixtures;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// NamingDerivation against the manifest the frontend produced. The
    /// derivation is a second copy of codegen/core/src/naming.ts, and a
    /// generated spec now carries a physical name only where the manifest
    /// diverges from that rule — so for the ordinary host the runtime's
    /// copy is not a fallback, it is the only place the name exists. If
    /// the two rules drift apart, a generated host reads and writes
    /// tables its own schema SQL never created, and nothing else in the
    /// suite would notice: the emitter goldens pin bytes the copy is
    /// absent from, and the integration fixtures build both halves from
    /// the same drifted copy.
    ///
    /// A failure here is not "update the expectation". Either
    /// naming.ts changed and this copy did not, or the copy changed and
    /// naming.ts did not; the two have to be made to agree.
    /// </summary>
    public class NamingDerivationManifestTests
    {
        private static SqliteHostNaming NamingOf(JsonElement manifest)
        {
            JsonElement naming = manifest.GetProperty("naming");
            return new SqliteHostNaming(
                naming.GetProperty("callTablePrefix").GetString(),
                naming.GetProperty("resultTablePrefix").GetString(),
                naming.GetProperty("inputColumnPrefix").GetString(),
                naming.GetProperty("resultColumnPrefix").GetString(),
                naming.GetProperty("inputListTableInfix").GetString(),
                naming.GetProperty("resultListTableInfix").GetString(),
                manifest.GetProperty("queueTable").GetProperty("name").GetString(),
                manifest.GetProperty("inputsTable").GetProperty("name").GetString(),
                manifest.GetProperty("varsTable").GetProperty("name").GetString(),
                manifest.GetProperty("controlTable").GetProperty("name").GetString(),
                naming.GetProperty("functionPrefix").GetString());
        }

        [Theory]
        [InlineData("sample-host.manifest.json")]
        [InlineData("high-api-host.manifest.json")]
        [InlineData("syntax-floor-host.manifest.json")]
        // The only row that can fail on a hardcoded default: every prefix,
        // infix and column name in this host differs from the protocol
        // one, so a derivation that ignores the naming block still agrees
        // with the three rows above and disagrees with this one.
        [InlineData("custom-naming-host.manifest.json")]
        public void ResolvedNamesEqualWhatTheRuntimeDerives(string fileName)
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(FixturePaths.Manifest(fileName)));
            JsonElement manifest = document.RootElement;
            SqliteHostNaming naming = NamingOf(manifest);

            int checkedNames = 0;
            foreach (JsonElement method in manifest.GetProperty("methods").EnumerateArray())
            {
                string methodName = method.GetProperty("methodName").GetString();

                Assert.Equal(
                    method.GetProperty("callTable").GetString(),
                    NamingDerivation.CallTable(naming, methodName));
                Assert.Equal(
                    method.GetProperty("resultTable").GetString(),
                    NamingDerivation.ResultTable(naming, methodName));
                Assert.Equal(
                    method.GetProperty("queueTrigger").GetString(),
                    NamingDerivation.QueueTrigger(naming, methodName));
                checkedNames += 3;

                checkedNames += CheckShape(naming, methodName, method.GetProperty("input"), true);
                checkedNames += CheckShape(naming, methodName, method.GetProperty("result"), false);
            }

            // The manifest has to exercise the derivation, or the assertions
            // above pass over an empty loop.
            Assert.True(checkedNames > 0, "the manifest declared no names to check");
        }

        private static int CheckShape(
            SqliteHostNaming naming,
            string methodName,
            JsonElement shape,
            bool isInput)
        {
            int count = 0;
            foreach (JsonElement field in shape.GetProperty("fields").EnumerateArray())
            {
                count += CheckColumn(naming, field, isInput);
            }
            foreach (JsonElement listField in shape.GetProperty("listFields").EnumerateArray())
            {
                string sqlName = listField.GetProperty("sqlName").GetString();
                Assert.Equal(
                    listField.GetProperty("childTable").GetString(),
                    isInput
                        ? NamingDerivation.InputListTable(naming, methodName, sqlName)
                        : NamingDerivation.ResultListTable(naming, methodName, sqlName));
                count++;
                foreach (JsonElement itemField in listField.GetProperty("itemFields").EnumerateArray())
                {
                    count += CheckColumn(naming, itemField, isInput);
                }
            }
            return count;
        }

        private static int CheckColumn(SqliteHostNaming naming, JsonElement field, bool isInput)
        {
            string sqlName = field.GetProperty("sqlName").GetString();
            Assert.Equal(
                field.GetProperty("column").GetString(),
                isInput
                    ? NamingDerivation.InputColumn(naming, sqlName)
                    : NamingDerivation.ResultColumn(naming, sqlName));
            return 1;
        }
    }
}
