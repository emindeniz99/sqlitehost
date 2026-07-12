using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SqliteHost.Adapters.Native;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Pins which native sqlite3 the SqliteHost.Adapters.Native package
    /// loads for the whole test run. The package itself is
    /// netstandard2.0-pure DllImport("sqlite3") — resolution is the OS
    /// loader's job — so the net8 test host installs a DllImportResolver on
    /// the package's assembly that resolves that one name:
    ///
    ///  1. SQLITEHOST_NATIVE_SQLITE, when set (the compatibility matrix,
    ///     tests/compatibility-sqlite/run-matrix.sh): the native adapter
    ///     honors the same override as the SQLitePCLRaw dynamic provider,
    ///     so matrix cells exercise it against the same pinned build;
    ///  2. otherwise the newest cached matrix build under
    ///     tests/compatibility-sqlite/.cache/libsqlite3-*.so, if present;
    ///  3. otherwise the system "libsqlite3.so.0";
    ///  4. otherwise default OS-loader resolution.
    ///
    /// This is a SEPARATE mechanism from Adapter/NativeSqliteOverride.cs:
    /// that [ModuleInitializer] pins the SQLitePCLRaw *provider* used by
    /// Microsoft.Data.Sqlite / sqlite-net and stays untouched; this one
    /// only governs the P/Invoke package's assembly. Both initializers run
    /// before any test code in this assembly executes.
    /// </summary>
    internal static class NativeAdapterLibraryResolver
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            NativeLibrary.SetDllImportResolver(
                typeof(NativeSqliteHostConnection).Assembly, Resolve);
        }

        private static IntPtr Resolve(
            string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != NativeSqliteHostConnectionFactory.NativeLibraryPath)
            {
                return IntPtr.Zero;   // not ours: default resolution
            }

            string overridePath = NativeSqliteOverride.NativeLibraryPath;
            if (!string.IsNullOrEmpty(overridePath))
            {
                // Matrix runs must fail loud when the pinned build is bad,
                // never fall through to some other sqlite3.
                return NativeLibrary.Load(overridePath);
            }

            string cachedPath = NewestCachedMatrixBuild();
            if (cachedPath != null && NativeLibrary.TryLoad(cachedPath, out IntPtr cached))
            {
                return cached;
            }

            if (NativeLibrary.TryLoad("libsqlite3.so.0", out IntPtr system))
            {
                return system;
            }

            return IntPtr.Zero;   // fall back to default OS-loader resolution
        }

        /// <summary>
        /// Highest-versioned tests/compatibility-sqlite/.cache/libsqlite3-&lt;ver&gt;.so
        /// (built by run-matrix.sh), located by walking up from the test
        /// base directory; null when the repo cache is absent.
        /// </summary>
        private static string NewestCachedMatrixBuild()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                string cacheDirectory = Path.Combine(
                    directory.FullName, "tests", "compatibility-sqlite", ".cache");
                if (!Directory.Exists(cacheDirectory))
                {
                    continue;
                }
                string newestPath = null;
                Version newestVersion = null;
                foreach (string candidate in Directory.GetFiles(cacheDirectory, "libsqlite3-*.so"))
                {
                    string name = Path.GetFileNameWithoutExtension(candidate);
                    if (Version.TryParse(name.Substring("libsqlite3-".Length), out Version version)
                        && (newestVersion == null || version > newestVersion))
                    {
                        newestVersion = version;
                        newestPath = candidate;
                    }
                }
                return newestPath;
            }
            return null;
        }
    }
}
