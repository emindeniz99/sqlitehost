using System.IO;

namespace SqliteHost.Adapters.Native
{
    /// <summary>
    /// Workspace factory over <see cref="NativeSqliteHostConnection"/>.
    /// Opens a private in-memory database by default; the overload taking a
    /// path recreates that file fresh for every workspace — the runtime's
    /// workspace is temporary and schema creation assumes an empty
    /// database, so any existing file (and its -wal/-shm siblings) at the
    /// path is DELETED on each open. See the constructor overload.
    /// Carries the static scalar-function capability marker
    /// (<see cref="ISqliteHostScalarFunctionCapableFactory"/>): connections
    /// implement ISqliteHostScalarFunctionConnection natively.
    /// </summary>
    public sealed class NativeSqliteHostConnectionFactory : ISqliteHostScalarFunctionCapableFactory
    {
        private readonly string _databasePath;

        /// <summary>In-memory workspaces (":memory:").</summary>
        public NativeSqliteHostConnectionFactory()
            : this(":memory:")
        {
        }

        /// <summary>
        /// File-backed workspaces at <paramref name="scratchDatabasePath"/>.
        /// DESTRUCTIVE: the path is scratch space, not storage — any file
        /// already there, plus its -wal and -shm siblings, is deleted on
        /// every <see cref="OpenWorkspace"/> call, which the runtime makes
        /// once per Run. Never point this at a database you want to keep
        /// (a save file, an asset database); use a temporary path such as
        /// <c>Path.Combine(Path.GetTempPath(), "sqlitehost-workspace.db")</c>.
        /// Documented in docs/adapter-contract.md (Workspace lifecycle).
        /// </summary>
        public NativeSqliteHostConnectionFactory(string scratchDatabasePath)
        {
            _databasePath = scratchDatabasePath;
        }

        /// <summary>
        /// The logical native library name every DllImport in this package
        /// targets: "sqlite3". This package never loads the library itself —
        /// resolution is the OS loader's job (it probes platform spellings
        /// such as libsqlite3.so / libsqlite3.dylib / sqlite3.dll /
        /// __Internal). Linux and macOS carry a system libsqlite3; Windows
        /// ships no sqlite3.dll, so a host there has to supply one.
        ///
        /// Supply it by ABSOLUTE PATH, not by search order. The assembly
        /// carries [DefaultDllImportSearchPaths] restricting the Windows
        /// loader to the assembly directory, the application directory and
        /// System32, because the default order also searches the current
        /// directory and every %PATH% entry — one user-writable directory
        /// there is enough for a planted sqlite3.dll to be loaded into the
        /// application. So: ship sqlite3.dll next to the application, or, on
        /// .NET Core / .NET 5+, install
        /// System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver
        /// on typeof(NativeSqliteHostConnection).Assembly and map this name
        /// to an absolute path you trust — the SqliteHost repo's test project
        /// (csharp/SqliteHost.Tests/Adapter/NativeAdapterLibraryResolver.cs)
        /// demonstrates the mechanism while driving the real-engine version
        /// matrix. Never place the library on %PATH% and rely on the search.
        ///
        /// Unity IL2CPP does not honor [DefaultDllImportSearchPaths]: an
        /// IL2CPP consumer gets no protection from it and must vendor the
        /// native library next to the player, or resolve an absolute path
        /// itself.
        /// </summary>
        public static string NativeLibraryPath => NativeMethods.LibraryName;

        public ISqliteHostConnection OpenWorkspace()
        {
            if (_databasePath != ":memory:")
            {
                DeleteIfExists(_databasePath);
                DeleteIfExists(_databasePath + "-wal");
                DeleteIfExists(_databasePath + "-shm");
            }
            return NativeSqliteHostConnection.Open(_databasePath);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
