using System;
using System.Runtime.InteropServices;
using System.Text;

// Every DllImport in this assembly targets the bare name "sqlite3", and on
// Windows the default loader search order includes the current directory and
// every %PATH% entry — so a user-writable directory on %PATH% is enough for
// an attacker-planted sqlite3.dll to be bound instead of the real engine and
// to run its DllMain inside the consuming application. Restricting the
// search to the assembly's own directory, the application directory and
// System32 removes those hijack positions; a host that needs a library
// elsewhere maps it explicitly through a DllImportResolver
// (see NativeSqliteHostConnectionFactory.NativeLibraryPath).
//
// Honored by .NET Core / .NET 5+ and by .NET Framework 4.6.1+. It is NOT
// honored under Unity IL2CPP or Mono, which have their own probing rules:
// an IL2CPP consumer must ship the native library next to the player (or
// resolve an absolute path itself) rather than rely on this attribute.
[assembly: DefaultDllImportSearchPaths(
    DllImportSearchPath.AssemblyDirectory
    | DllImportSearchPath.ApplicationDirectory
    | DllImportSearchPath.System32)]

namespace SqliteHost.Adapters.Native
{
    /// <summary>
    /// Raw P/Invoke surface over libsqlite3 (netstandard2.0-pure: IntPtr +
    /// Marshal, no unsafe code). Every DllImport targets the logical library
    /// name "sqlite3"; resolving that name to an actual binary is the OS
    /// loader's job (see
    /// <see cref="NativeSqliteHostConnectionFactory.NativeLibraryPath"/>).
    ///
    /// All text crossing the boundary is UTF-8: strings are marshalled
    /// manually as NUL-terminated byte arrays on the way in and read back
    /// byte-by-byte on the way out (netstandard2.0 has no
    /// Marshal.PtrToStringUTF8).
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>Logical library name every DllImport in this package targets.</summary>
        internal const string LibraryName = "sqlite3";

        // ---- result codes -------------------------------------------------

        internal const int SQLITE_OK = 0;
        internal const int SQLITE_ROW = 100;
        internal const int SQLITE_DONE = 101;

        // ---- fundamental datatypes (sqlite3_column_type / value_type) -----

        internal const int SQLITE_INTEGER = 1;
        internal const int SQLITE_FLOAT = 2;
        internal const int SQLITE_TEXT = 3;
        internal const int SQLITE_BLOB = 4;
        internal const int SQLITE_NULL = 5;

        // ---- text encodings (sqlite3_create_function_v2 eTextRep) ---------
        // v1 rule (docs/adapter-contract.md): SQLITE_UTF8 only, never
        // combined with SQLITE_DETERMINISTIC.

        internal const int SQLITE_UTF8 = 1;

        // ---- open flags ----------------------------------------------------

        internal const int SQLITE_OPEN_READWRITE = 0x2;
        internal const int SQLITE_OPEN_CREATE = 0x4;

        /// <summary>
        /// Special destructor value telling SQLite to make its own private
        /// copy of bound/returned text and blob bytes before the call
        /// returns, so passing short-lived managed arrays is safe.
        /// </summary>
        internal static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        // ---- callback delegate shapes --------------------------------------
        // Instances of these delegates are kept alive in static readonly
        // fields on NativeSqliteHostConnection so the marshalled thunks can
        // never be garbage collected while SQLite might still call them.

        /// <summary>xFunc for sqlite3_create_function_v2: (sqlite3_context*, int argc, sqlite3_value** argv).</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ScalarFunctionCallback(IntPtr context, int argCount, IntPtr args);

        /// <summary>xDestroy for sqlite3_create_function_v2: (void* pApp).</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void FunctionDestroyCallback(IntPtr userData);

        // ---- connection lifecycle ------------------------------------------

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2(byte[] filenameUtf8, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close_v2(IntPtr db);

        // ---- statement lifecycle -------------------------------------------

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(
            IntPtr db, byte[] sqlUtf8, int byteCount, out IntPtr statement, out IntPtr tail);

        /// <summary>
        /// IntPtr-sql overload for callers that need the returned tail
        /// pointer: with the byte[] overload the array's pinning ends when
        /// the call returns, so the tail (which points into that buffer)
        /// must not be dereferenced afterwards. Callers pin the SQL bytes
        /// explicitly, pass their address here, and convert the tail to an
        /// offset while still pinned.
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(
            IntPtr db, IntPtr sqlUtf8, int byteCount, out IntPtr statement, out IntPtr tail);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr statement);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr statement);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_reset(IntPtr statement);

        // ---- parameters ------------------------------------------------------

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_parameter_count(IntPtr statement);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_bind_parameter_name(IntPtr statement, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_parameter_index(IntPtr statement, byte[] nameUtf8);

        // ---- binding ----------------------------------------------------------

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_null(IntPtr statement, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_int(IntPtr statement, int index, int value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_double(IntPtr statement, int index, double value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_text(
            IntPtr statement, int index, byte[] valueUtf8, int byteCount, IntPtr destructor);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_blob(
            IntPtr statement, int index, byte[] value, int byteCount, IntPtr destructor);

        /// <summary>
        /// Used for zero-length blobs: marshalling an empty managed array
        /// could hand SQLite a null pointer, which sqlite3_bind_blob treats
        /// as binding NULL — zeroblob(0) deterministically binds an empty
        /// (non-NULL) blob instead.
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_zeroblob(IntPtr statement, int index, int byteCount);

        // ---- columns -----------------------------------------------------------

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_count(IntPtr statement);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_type(IntPtr statement, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_int(IntPtr statement, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_column_int64(IntPtr statement, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern double sqlite3_column_double(IntPtr statement, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text(IntPtr statement, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_bytes(IntPtr statement, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_blob(IntPtr statement, int index);

        // ---- errors --------------------------------------------------------------

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_errcode(IntPtr db);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_extended_errcode(IntPtr db);

        // ---- version ---------------------------------------------------------------

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_libversion();

        // ---- scalar functions --------------------------------------------------------

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_create_function_v2(
            IntPtr db,
            byte[] nameUtf8,
            int argCount,
            int textRep,
            IntPtr userData,
            ScalarFunctionCallback func,
            IntPtr step,
            IntPtr final,
            FunctionDestroyCallback destroy);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_result_null(IntPtr context);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_result_int64(IntPtr context, long value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_result_double(IntPtr context, double value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_result_text(
            IntPtr context, byte[] valueUtf8, int byteCount, IntPtr destructor);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_result_blob(
            IntPtr context, byte[] value, int byteCount, IntPtr destructor);

        /// <summary>Same rationale as <see cref="sqlite3_bind_zeroblob"/> for empty function results.</summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_result_zeroblob(IntPtr context, int byteCount);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_result_error(IntPtr context, byte[] messageUtf8, int byteCount);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_value_type(IntPtr value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_value_int64(IntPtr value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern double sqlite3_value_double(IntPtr value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_value_text(IntPtr value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_value_bytes(IntPtr value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_value_blob(IntPtr value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_user_data(IntPtr context);

        // ---- UTF-8 marshalling helpers -------------------------------------------------

        /// <summary>Encodes a string as NUL-terminated UTF-8 bytes (never null, never empty).</summary>
        internal static byte[] ToUtf8Z(string value)
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            var bytes = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
            return bytes;   // trailing 0 left by array initialization
        }

        /// <summary>Reads a NUL-terminated UTF-8 string (null pointer maps to null).</summary>
        internal static string FromUtf8Z(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                return null;
            }
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0)
            {
                length++;
            }
            return FromUtf8(pointer, length);
        }

        /// <summary>Reads UTF-8 bytes of a known length (null pointer maps to null).</summary>
        internal static string FromUtf8(IntPtr pointer, int byteCount)
        {
            if (pointer == IntPtr.Zero)
            {
                return null;
            }
            if (byteCount <= 0)
            {
                return string.Empty;
            }
            var bytes = new byte[byteCount];
            Marshal.Copy(pointer, bytes, 0, byteCount);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>Copies native bytes of a known length (null pointer maps to an empty array, never null).</summary>
        internal static byte[] CopyBytes(IntPtr pointer, int byteCount)
        {
            if (pointer == IntPtr.Zero || byteCount <= 0)
            {
                return new byte[0];
            }
            var bytes = new byte[byteCount];
            Marshal.Copy(pointer, bytes, 0, byteCount);
            return bytes;
        }
    }
}
