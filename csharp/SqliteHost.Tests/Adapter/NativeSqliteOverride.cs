using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SQLitePCL;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Pins the SQLitePCLRaw provider for the whole test run, once, before
    /// any Microsoft.Data.Sqlite (or sqlite-net) code can call
    /// SQLitePCL.Batteries_V2.Init() over it.
    ///
    /// When SQLITEHOST_NATIVE_SQLITE=&lt;path to a sqlite3 shared library&gt; is
    /// set, a dynamic provider (SQLitePCLRaw.provider.dynamic_cdecl) is
    /// installed that resolves every sqlite3_* export from that library via
    /// System.Runtime.InteropServices.NativeLibrary — so the test suite runs
    /// against an arbitrary real SQLite build instead of the bundled
    /// e_sqlite3. Otherwise the normal bundle provider is initialized.
    ///
    /// Either way the provider is then frozen (raw.FreezeProvider()), which
    /// turns the later Batteries_V2.Init() calls made by SqliteConnection /
    /// sqlite-net static initialization into no-ops, guaranteeing our choice
    /// wins. A [ModuleInitializer] runs before any test code in this
    /// assembly executes.
    ///
    /// Scope note: this override only governs adapters built on SQLitePCLRaw
    /// (Microsoft.Data.Sqlite, sqlite-net). System.Data.SQLite has its own
    /// interop layer with its own bundled native and is not affected; its
    /// integration tests are skipped while the override is active (see
    /// IntegrationFixtureTests).
    /// </summary>
    internal static class NativeSqliteOverride
    {
        internal const string PathVariable = "SQLITEHOST_NATIVE_SQLITE";
        internal const string ExpectedVersionVariable = "SQLITEHOST_EXPECTED_SQLITE_VERSION";
        internal const string ExpectedVersionNumberVariable = "SQLITEHOST_EXPECTED_SQLITE_VERSION_NUMBER";

        internal static string NativeLibraryPath =>
            Environment.GetEnvironmentVariable(PathVariable);

        internal static string ExpectedVersion =>
            Environment.GetEnvironmentVariable(ExpectedVersionVariable);

        /// <summary>sqlite3_libversion_number encoding (major*1000000 + minor*1000 + patch).</summary>
        internal static string ExpectedVersionNumber =>
            Environment.GetEnvironmentVariable(ExpectedVersionNumberVariable);

        /// <summary>True when the test run targets an explicit native SQLite build.</summary>
        internal static bool IsActive => !string.IsNullOrEmpty(NativeLibraryPath);

        [ModuleInitializer]
        internal static void Initialize()
        {
            string path = NativeLibraryPath;
            if (string.IsNullOrEmpty(path))
            {
                // Deterministic default: the bundled provider, frozen so no
                // later Init() can swap it mid-run (both bundle_e_sqlite3 and
                // bundle_green are on the test project's dependency graph).
                Batteries_V2.Init();
                raw.FreezeProvider();
                return;
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    PathVariable + " points at a native SQLite library that does not exist.", path);
            }

            IntPtr handle = NativeLibrary.Load(path);
            SQLite3Provider_dynamic_cdecl.Setup(
                "sqlitehost-native", new NativeLibraryFunctionPointers(handle));
            raw.SetProvider(new SQLite3Provider_dynamic_cdecl());
            raw.FreezeProvider();
        }

        private sealed class NativeLibraryFunctionPointers : IGetFunctionPointer
        {
            private readonly IntPtr _library;

            public NativeLibraryFunctionPointers(IntPtr library)
            {
                _library = library;
            }

            public IntPtr GetFunctionPointer(string name)
            {
                // Old SQLite builds legitimately lack newer exports; return
                // zero and let SQLitePCLRaw fail only if the API is called.
                return NativeLibrary.TryGetExport(_library, name, out IntPtr address)
                    ? address
                    : IntPtr.Zero;
            }
        }
    }
}
