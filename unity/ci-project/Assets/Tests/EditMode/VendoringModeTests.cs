// Which vendoring build the editor actually compiled, checked against the
// one .github/workflows/unity-ci.yml asked this leg for.
//
// SQLITEHOST_SLIM strips the package's optional strict checks
// (docs/csharp-api.md). Until the matrix started alternating it, no Unity
// editor had ever compiled that build: the CI project has no committed
// ProjectSettings.asset to hold a scripting define, SqliteHost.asmdef has
// no versionDefines, and the editor command line has no flag for one. The
// size claims rest on a build nothing in an editor had seen.
//
// The mechanism is a csc.rsp the workflow drops next to the package's
// asmdef. If it ever stops working — a Unity release that reads response
// files differently, a package layout change that moves the asmdef — the
// slim legs would compile the full build and pass, which is precisely the
// failure this file exists to make loud. So the check has two independent
// sides: Assets/sqlitehost-build.txt records what the leg ASKED for, and
// reflection over the compiled package assembly says what it GOT.
//
// SqlParameterScanner is the package's one whole-file #if !SQLITEHOST_SLIM,
// so its presence in the assembly is the build's signature. It is looked up
// by name rather than referenced: a direct reference would not compile under
// the slim build, which would turn this test's own compilation into the
// assertion instead of measuring the package's.

using System;
using System.IO;
using NUnit.Framework;
using SqliteHost;
using UnityEngine;

namespace SqliteHostCiProject.Tests
{
    public sealed class VendoringModeTests
    {
        private const string MarkerFileName = "sqlitehost-build.txt";
        private const string StrippedTypeName = "SqliteHost.SqlParameterScanner";

        [Test]
        public void PackageCompiledTheVendoringBuildThisLegAskedFor()
        {
            string markerPath = Path.Combine(Application.dataPath, MarkerFileName);
            if (!File.Exists(markerPath))
            {
                // Opened by hand rather than by the workflow. Nothing to
                // compare against, and no reason to fail a local run.
                Assert.Ignore(
                    "no Assets/" + MarkerFileName + ": this project was opened outside "
                    + ".github/workflows/unity-ci.yml, which writes that file naming the leg's build");
            }

            string requested = File.ReadAllText(markerPath).Trim();
            Assert.IsTrue(
                requested == "full" || requested == "slim",
                "Assets/" + MarkerFileName + " must hold \"full\" or \"slim\", found: " + requested);

            Type stripped = typeof(SqliteHostRuntime<object>).Assembly.GetType(StrippedTypeName);
            string compiled = stripped == null ? "slim" : "full";

            Assert.AreEqual(
                requested,
                compiled,
                "the workflow asked this leg for the " + requested + " vendoring build, but the package "
                + "assembly compiled " + compiled + " (" + StrippedTypeName + " is "
                + (stripped == null ? "absent" : "present")
                + "). The csc.rsp next to the package's asmdef is what carries "
                + "-define:SQLITEHOST_SLIM; if Unity has stopped reading it, the slim legs are measuring "
                + "the full build.");
        }
    }
}
