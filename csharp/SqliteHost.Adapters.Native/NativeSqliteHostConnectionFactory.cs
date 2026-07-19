using System.IO;

namespace SqliteHost.Adapters.Native
{
    /// <summary>
    /// Workspace factory over <see cref="NativeSqliteHostConnection"/>.
    /// Opens a private in-memory database by default; the overload taking a
    /// database path recreates that file fresh for every workspace — the
    /// runtime's workspace is temporary and schema creation assumes an
    /// empty database, so any existing file (and its -wal/-shm siblings)
    /// at the path is deleted on each open.
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

        /// <summary>File-backed workspaces at <paramref name="databasePath"/>.</summary>
        public NativeSqliteHostConnectionFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        /// <summary>
        /// The logical native library name every DllImport in this package
        /// targets: "sqlite3". This package never loads the library itself —
        /// resolution is the OS loader's job (it probes platform spellings
        /// such as libsqlite3.so / libsqlite3.dylib / sqlite3.dll /
        /// __Internal on the platform search path). Hosts on .NET 5+ that
        /// must pin a specific build can install
        /// System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver
        /// on typeof(NativeSqliteHostConnection).Assembly and map this name
        /// to any absolute path (the SqliteHost repo's test project does
        /// exactly that to drive the real-engine version matrix).
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
