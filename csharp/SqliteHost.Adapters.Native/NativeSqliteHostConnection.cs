using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SqliteHost.Adapters.Native
{
    /// <summary>
    /// SqliteHost adapter over a platform-provided libsqlite3 consumed
    /// directly via DllImport/P/Invoke — the pattern Unity SQLite wrappers
    /// (SQLite4Unity3d and friends) use, with no managed wrapper dependency.
    /// Implements the full adapter contract (docs/adapter-contract.md):
    ///
    ///  - Execute/Query via prepare/bind/step/finalize; every native result
    ///    code other than OK/ROW/DONE surfaces as
    ///    <see cref="SqliteHostAdapterException"/> carrying the extended
    ///    error code and sqlite3_errmsg text;
    ///  - bare binding names resolve against every :name / @name / $name
    ///    occurrence (sqlite3_bind_parameter_index over all three
    ///    prefixes); a statement parameter without a binding refuses to
    ///    execute instead of running as an implicit NULL;
    ///  - the optional inline scalar-function capability is implemented
    ///    natively via sqlite3_create_function_v2 (SQLITE_UTF8 only, no
    ///    SQLITE_DETERMINISTIC — v1 rule).
    ///
    /// Unity IL2CPP caveat: the two reverse-P/Invoke callbacks
    /// (<see cref="ScalarFunctionThunk"/> and
    /// <see cref="ReleaseRegistrationHandle"/>) are static methods, as
    /// IL2CPP requires, but IL2CPP additionally requires them to carry
    /// [AOT.MonoPInvokeCallback(typeof(...))] — a Unity-only attribute a
    /// netstandard2.0 library cannot reference. Unity consumers vendoring
    /// these sources must add that one attribute to those two methods; the
    /// Execute/Query/Prepare path is callback-free and IL2CPP-clean as-is.
    /// </summary>
    public sealed class NativeSqliteHostConnection
        : ISqliteHostConnection, ISqliteHostPrepareConnection, ISqliteHostScalarFunctionConnection
    {
        private IntPtr _db;
        private bool _disposed;

        /// <summary>
        /// Statements created via <see cref="Prepare"/> that have not been
        /// disposed yet; Dispose finalizes them before closing the handle.
        /// </summary>
        private readonly List<NativePreparedStatement> _liveStatements = new List<NativePreparedStatement>();

        // Static delegate instances passed to sqlite3_create_function_v2.
        // Holding them in static readonly fields keeps the marshalled
        // native thunks alive for the process lifetime, so SQLite can call
        // them long after the registering stack frame is gone.
        private static readonly NativeMethods.ScalarFunctionCallback ScalarFunctionThunkDelegate = ScalarFunctionThunk;
        private static readonly NativeMethods.FunctionDestroyCallback ReleaseRegistrationDelegate = ReleaseRegistrationHandle;

        private NativeSqliteHostConnection(IntPtr db)
        {
            _db = db;
        }

        /// <summary>Runtime SQLite version string (sqlite3_libversion), e.g. "3.53.3".</summary>
        public static string SqliteLibVersion
            => NativeMethods.FromUtf8Z(NativeMethods.sqlite3_libversion());

        /// <summary>Opens a private in-memory database.</summary>
        public static NativeSqliteHostConnection OpenInMemory()
        {
            return Open(":memory:");
        }

        /// <summary>Opens (creating if needed) the database file at <paramref name="databasePath"/>.</summary>
        public static NativeSqliteHostConnection Open(string databasePath)
        {
            if (string.IsNullOrEmpty(databasePath))
            {
                throw new ArgumentException("databasePath must be non-empty.", nameof(databasePath));
            }
            int rc = NativeMethods.sqlite3_open_v2(
                NativeMethods.ToUtf8Z(databasePath),
                out IntPtr db,
                NativeMethods.SQLITE_OPEN_READWRITE | NativeMethods.SQLITE_OPEN_CREATE,
                IntPtr.Zero);
            if (rc != NativeMethods.SQLITE_OK)
            {
                // Even on failure sqlite3_open_v2 usually hands back a
                // handle that carries the error message and must be closed.
                string message = db != IntPtr.Zero
                    ? NativeMethods.FromUtf8Z(NativeMethods.sqlite3_errmsg(db))
                    : "out of memory";
                if (db != IntPtr.Zero)
                {
                    NativeMethods.sqlite3_close_v2(db);
                }
                throw new SqliteHostAdapterException(
                    "sqlite3_open_v2 failed (" + rc + "): " + message, rc, null);
            }
            return new NativeSqliteHostConnection(db);
        }

        public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            ThrowIfDisposed();
            IntPtr statement = PrepareAndBind(sql, bindings);
            try
            {
                // SQLite evaluates a row-producing statement only as it is
                // stepped: drain to SQLITE_DONE (discarding rows) so
                // later-row evaluation — errors and inline function
                // invocations — is never silently skipped
                // (docs/adapter-contract.md).
                int rc;
                while ((rc = NativeMethods.sqlite3_step(statement)) == NativeMethods.SQLITE_ROW)
                {
                }
                if (rc != NativeMethods.SQLITE_DONE)
                {
                    throw CreateError("sqlite3_step", rc);
                }
            }
            finally
            {
                NativeMethods.sqlite3_finalize(statement);
            }
        }

        public IReadOnlyList<object> QueryRows(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, object> mapper)
        {
            ThrowIfDisposed();
            IntPtr statement = PrepareAndBind(sql, bindings);
            var row = new NativeRow(statement);
            try
            {
                var results = new List<object>();
                while (true)
                {
                    int rc = NativeMethods.sqlite3_step(statement);
                    if (rc == NativeMethods.SQLITE_ROW)
                    {
                        results.Add(mapper(row));
                        continue;
                    }
                    if (rc == NativeMethods.SQLITE_DONE)
                    {
                        return results;
                    }
                    throw CreateError("sqlite3_step", rc);
                }
            }
            finally
            {
                row.Invalidate();   // a row view captured by the mapper must not outlive the statement
                NativeMethods.sqlite3_finalize(statement);
            }
        }

        public ISqliteHostPreparedStatement Prepare(string sql)
        {
            ThrowIfDisposed();
            var statement = new NativePreparedStatement(this, PrepareOnly(sql));
            _liveStatements.Add(statement);
            return statement;
        }

        /// <summary>
        /// Optional capability (docs/adapter-contract.md), implemented
        /// natively: sqlite3_create_function_v2 once per arity in
        /// MinArgs..MaxArgs, eTextRep = SQLITE_UTF8 only (no
        /// SQLITE_DETERMINISTIC in v1). Each registration allocates a
        /// GCHandle around the function object and passes it as pApp; the
        /// static xFunc callback resolves it via sqlite3_user_data, and the
        /// static xDestroy callback frees it when SQLite drops the
        /// registration (overload with the same name/arity, failed
        /// registration, or connection close) — so handle lifetime exactly
        /// tracks the native registration's lifetime.
        /// </summary>
        public void RegisterScalarFunction(SqliteHostScalarFunction function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }
            ThrowIfDisposed();
            byte[] nameUtf8 = NativeMethods.ToUtf8Z(function.Name);
            for (int arity = function.MinArgs; arity <= function.MaxArgs; arity++)
            {
                GCHandle registration = GCHandle.Alloc(function);
                int rc = NativeMethods.sqlite3_create_function_v2(
                    _db,
                    nameUtf8,
                    arity,
                    NativeMethods.SQLITE_UTF8,
                    GCHandle.ToIntPtr(registration),
                    ScalarFunctionThunkDelegate,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    ReleaseRegistrationDelegate);
                if (rc != NativeMethods.SQLITE_OK)
                {
                    // On failure SQLite has already invoked xDestroy on this
                    // registration's user data (measured on 3.9.0 through the
                    // newest amalgamation), so the GCHandle is freed — do not
                    // free it again here.
                    throw CreateError("sqlite3_create_function_v2", rc);
                }
            }
        }

        /// <summary>
        /// xFunc thunk shared by every registration. Nothing may escape this
        /// frame (IL2CPP safety): argument marshalling, Invoke, and result
        /// writing are all inside the catch; any failure is reported through
        /// sqlite3_result_error with the SQLITEHOST_HANDLER_ERROR: marker so
        /// the runtime maps it to FailedHandler/handler-error.
        /// Unity IL2CPP consumers must add
        /// [AOT.MonoPInvokeCallback(typeof(NativeMethods.ScalarFunctionCallback))] here.
        /// </summary>
        private static void ScalarFunctionThunk(IntPtr context, int argCount, IntPtr args)
        {
            try
            {
                var function = (SqliteHostScalarFunction)GCHandle
                    .FromIntPtr(NativeMethods.sqlite3_user_data(context))
                    .Target;
                var arguments = new SqliteHostBindingValue[argCount];
                for (int i = 0; i < argCount; i++)
                {
                    arguments[i] = ReadFunctionArgument(Marshal.ReadIntPtr(args, i * IntPtr.Size));
                }
                SqliteHostBindingValue result = function.Invoke(arguments);
                WriteFunctionResult(context, result ?? SqliteHostBindingValue.Null());
            }
            catch (Exception ex)
            {
                ReportHandlerError(context, ex);
            }
        }

        private static void ReportHandlerError(IntPtr context, Exception ex)
        {
            try
            {
                byte[] messageUtf8 = NativeMethods.ToUtf8Z(
                    SqliteHostScalarFunction.HandlerErrorMarker + " " + ex.Message);
                NativeMethods.sqlite3_result_error(context, messageUtf8, messageUtf8.Length - 1);
            }
            catch
            {
                // Nothing may escape the native callback frame; if even the
                // error path fails, SQLite still sees the default result.
            }
        }

        /// <summary>
        /// xDestroy thunk: frees the one GCHandle owned by the registration
        /// SQLite is dropping. Unity IL2CPP consumers must add
        /// [AOT.MonoPInvokeCallback(typeof(NativeMethods.FunctionDestroyCallback))] here.
        /// </summary>
        private static void ReleaseRegistrationHandle(IntPtr userData)
        {
            try
            {
                GCHandle.FromIntPtr(userData).Free();
            }
            catch
            {
                // Nothing may escape the native callback frame.
            }
        }

        private static SqliteHostBindingValue ReadFunctionArgument(IntPtr value)
        {
            switch (NativeMethods.sqlite3_value_type(value))
            {
                case NativeMethods.SQLITE_INTEGER:
                    return SqliteHostBindingValue.Int64(NativeMethods.sqlite3_value_int64(value));
                case NativeMethods.SQLITE_FLOAT:
                    return SqliteHostBindingValue.Float64(NativeMethods.sqlite3_value_double(value));
                case NativeMethods.SQLITE_TEXT:
                {
                    // Per the C API docs, fetch the pointer first and the
                    // byte count second.
                    IntPtr text = NativeMethods.sqlite3_value_text(value);
                    int byteCount = NativeMethods.sqlite3_value_bytes(value);
                    return SqliteHostBindingValue.Text(NativeMethods.FromUtf8(text, byteCount) ?? string.Empty);
                }
                case NativeMethods.SQLITE_BLOB:
                {
                    IntPtr blob = NativeMethods.sqlite3_value_blob(value);
                    int byteCount = NativeMethods.sqlite3_value_bytes(value);
                    return SqliteHostBindingValue.Blob(NativeMethods.CopyBytes(blob, byteCount));
                }
                default:
                    return SqliteHostBindingValue.Null();
            }
        }

        private static void WriteFunctionResult(IntPtr context, SqliteHostBindingValue value)
        {
            switch (value.Type)
            {
                case SqliteHostBindingType.Int32:
                    NativeMethods.sqlite3_result_int64(context, value.Int32Value);
                    break;
                case SqliteHostBindingType.Int64:
                    NativeMethods.sqlite3_result_int64(context, value.Int64Value);
                    break;
                case SqliteHostBindingType.Bool:
                    NativeMethods.sqlite3_result_int64(context, value.BoolValue ? 1L : 0L);
                    break;
                case SqliteHostBindingType.Text:
                    if (value.TextValue == null)
                    {
                        NativeMethods.sqlite3_result_null(context);
                    }
                    else
                    {
                        byte[] textUtf8 = NativeMethods.ToUtf8Z(value.TextValue);
                        NativeMethods.sqlite3_result_text(
                            context, textUtf8, textUtf8.Length - 1, NativeMethods.SQLITE_TRANSIENT);
                    }
                    break;
                case SqliteHostBindingType.Blob:
                    if (value.BlobValue == null)
                    {
                        NativeMethods.sqlite3_result_null(context);
                    }
                    else if (value.BlobValue.Length == 0)
                    {
                        NativeMethods.sqlite3_result_zeroblob(context, 0);
                    }
                    else
                    {
                        NativeMethods.sqlite3_result_blob(
                            context, value.BlobValue, value.BlobValue.Length, NativeMethods.SQLITE_TRANSIENT);
                    }
                    break;
                case SqliteHostBindingType.Float32:
                    NativeMethods.sqlite3_result_double(context, (double)value.Float32Value);
                    break;
                case SqliteHostBindingType.Float64:
                    NativeMethods.sqlite3_result_double(context, value.Float64Value);
                    break;
                default:
                    NativeMethods.sqlite3_result_null(context);
                    break;
            }
        }

        /// <summary>
        /// Idempotent: finalizes all live prepared statements, then closes
        /// the handle via sqlite3_close_v2 (which also invokes xDestroy for
        /// every registered scalar function, freeing their GCHandles).
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            for (int i = _liveStatements.Count - 1; i >= 0; i--)
            {
                _liveStatements[i].FinalizeStatement();
            }
            _liveStatements.Clear();
            if (_db != IntPtr.Zero)
            {
                NativeMethods.sqlite3_close_v2(_db);
                _db = IntPtr.Zero;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                // Fail loud instead of letting a P/Invoke hit a null handle.
                throw new ObjectDisposedException(nameof(NativeSqliteHostConnection));
            }
        }

        private void RemoveLiveStatement(NativePreparedStatement statement)
        {
            _liveStatements.Remove(statement);
        }

        private IntPtr PrepareOnly(string sql)
        {
            byte[] sqlUtf8 = NativeMethods.ToUtf8Z(sql);
            // Pin explicitly and call the IntPtr overload: the tail pointer
            // points into the SQL buffer and is only meaningful while the
            // buffer is pinned, so convert it to an offset before unpinning.
            int rc;
            IntPtr statement;
            int tailOffset;
            GCHandle pin = GCHandle.Alloc(sqlUtf8, GCHandleType.Pinned);
            try
            {
                IntPtr basePtr = pin.AddrOfPinnedObject();
                rc = NativeMethods.sqlite3_prepare_v2(
                    _db, basePtr, sqlUtf8.Length, out statement, out IntPtr tail);
                tailOffset = tail == IntPtr.Zero
                    ? sqlUtf8.Length
                    : (int)(tail.ToInt64() - basePtr.ToInt64());
            }
            finally
            {
                pin.Free();
            }
            if (rc != NativeMethods.SQLITE_OK)
            {
                if (statement != IntPtr.Zero)
                {
                    NativeMethods.sqlite3_finalize(statement);
                }
                throw CreateError("sqlite3_prepare_v2", rc);
            }
            if (statement == IntPtr.Zero)
            {
                // Whitespace/comment-only SQL prepares "successfully" with no
                // statement; stepping a null handle would crash, and treating
                // it as success would mask an authoring error. Fail loud.
                throw new SqliteHostAdapterException(
                    "sqlite3_prepare_v2 produced no statement: the SQL text is empty or comment-only.",
                    0, null);
            }
            RejectSqlAfterFirstStatement(statement, sqlUtf8, tailOffset);
            return statement;
        }

        /// <summary>
        /// sqlite3_prepare_v2 compiles only the first statement and hands
        /// the rest back through the tail; running just the first while
        /// silently dropping the rest is exactly the silent partial
        /// execution docs/adapter-contract.md forbids. Preparing the tail
        /// (rather than scanning it for non-whitespace) deliberately
        /// tolerates trailing terminators, whitespace, and comments —
        /// mirroring SQLite's own "no statement" semantics above — so only
        /// a second executable statement (or a tail that fails to compile)
        /// is rejected. Nothing has been stepped when this throws.
        /// </summary>
        private void RejectSqlAfterFirstStatement(IntPtr statement, byte[] sqlUtf8, int tailOffset)
        {
            int tailLength = sqlUtf8.Length - tailOffset;
            if (tailLength <= 1)
            {
                return;   // nothing after the first statement but the NUL terminator
            }
            var tailUtf8 = new byte[tailLength];
            Array.Copy(sqlUtf8, tailOffset, tailUtf8, 0, tailLength);
            int rc = NativeMethods.sqlite3_prepare_v2(
                _db, tailUtf8, tailUtf8.Length, out IntPtr tailStatement, out IntPtr _);
            if (tailStatement != IntPtr.Zero)
            {
                NativeMethods.sqlite3_finalize(tailStatement);
            }
            if (rc != NativeMethods.SQLITE_OK || tailStatement != IntPtr.Zero)
            {
                NativeMethods.sqlite3_finalize(statement);
                throw new SqliteHostAdapterException(
                    "sqlite3_prepare_v2 left SQL after the first statement: multi-statement SQL"
                    + " is not supported; send each statement in its own Execute/Query/Prepare call.",
                    0, null);
            }
        }

        private IntPtr PrepareAndBind(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            IntPtr statement = PrepareOnly(sql);
            try
            {
                if (bindings != null)
                {
                    foreach (SqliteHostBinding binding in bindings)
                    {
                        BindUnderAllPrefixes(statement, binding);
                    }
                }
                RejectUnboundParameters(statement, bindings);
            }
            catch
            {
                NativeMethods.sqlite3_finalize(statement);
                throw;
            }
            return statement;
        }

        private static readonly string[] ParameterPrefixes = { ":", "@", "$" };

        private void BindUnderAllPrefixes(IntPtr statement, SqliteHostBinding binding)
        {
            // Bare names must bind every prefixed occurrence in the SQL;
            // prefixes absent from the statement resolve to index 0 and are
            // skipped, so binding keys not present are ignored here (the
            // runtime rejects leftovers as unused-binding before execution).
            foreach (string prefix in ParameterPrefixes)
            {
                int index = NativeMethods.sqlite3_bind_parameter_index(
                    statement, NativeMethods.ToUtf8Z(prefix + binding.Name));
                if (index > 0)
                {
                    BindValue(statement, index, binding.Value);
                }
            }
        }

        /// <summary>
        /// Adapter contract (docs/adapter-contract.md): a parameter the
        /// payload did not bind must never execute as a silent NULL. Raw
        /// sqlite3 leaves unbound parameters NULL, so verify every statement
        /// parameter received a binding before stepping.
        /// </summary>
        private static void RejectUnboundParameters(
            IntPtr statement, IReadOnlyList<SqliteHostBinding> bindings)
        {
            int count = NativeMethods.sqlite3_bind_parameter_count(statement);
            for (int i = 1; i <= count; i++)
            {
                string name = NativeMethods.FromUtf8Z(
                    NativeMethods.sqlite3_bind_parameter_name(statement, i));
                // Positional parameters (? and ?NNN) are unsupported by the
                // adapter contract and are never bound by
                // BindUnderAllPrefixes, so treat them as unbound even when a
                // binding named like the digits ("1" for ?1) exists.
                string bareName = string.IsNullOrEmpty(name) || name[0] == '?'
                    ? null
                    : name.Substring(1);
                bool bound = false;
                if (bareName != null && bindings != null)
                {
                    foreach (SqliteHostBinding binding in bindings)
                    {
                        if (binding.Name == bareName)
                        {
                            bound = true;
                            break;
                        }
                    }
                }
                if (!bound)
                {
                    throw new SqliteHostAdapterException(
                        "Parameter '" + (name ?? "?") + "' has no binding; refusing to execute with an implicit NULL.",
                        0, null);
                }
            }
        }

        private void BindValue(IntPtr statement, int index, SqliteHostBindingValue value)
        {
            int rc;
            switch (value.Type)
            {
                case SqliteHostBindingType.Int32:
                    rc = NativeMethods.sqlite3_bind_int(statement, index, value.Int32Value);
                    break;
                case SqliteHostBindingType.Int64:
                    rc = NativeMethods.sqlite3_bind_int64(statement, index, value.Int64Value);
                    break;
                case SqliteHostBindingType.Bool:
                    rc = NativeMethods.sqlite3_bind_int64(statement, index, value.BoolValue ? 1L : 0L);
                    break;
                case SqliteHostBindingType.Text:
                    if (value.TextValue == null)
                    {
                        rc = NativeMethods.sqlite3_bind_null(statement, index);
                    }
                    else
                    {
                        byte[] textUtf8 = NativeMethods.ToUtf8Z(value.TextValue);
                        rc = NativeMethods.sqlite3_bind_text(
                            statement, index, textUtf8, textUtf8.Length - 1, NativeMethods.SQLITE_TRANSIENT);
                    }
                    break;
                case SqliteHostBindingType.Blob:
                    if (value.BlobValue == null)
                    {
                        rc = NativeMethods.sqlite3_bind_null(statement, index);
                    }
                    else if (value.BlobValue.Length == 0)
                    {
                        rc = NativeMethods.sqlite3_bind_zeroblob(statement, index, 0);
                    }
                    else
                    {
                        rc = NativeMethods.sqlite3_bind_blob(
                            statement, index, value.BlobValue, value.BlobValue.Length, NativeMethods.SQLITE_TRANSIENT);
                    }
                    break;
                case SqliteHostBindingType.Float32:
                    rc = NativeMethods.sqlite3_bind_double(statement, index, (double)value.Float32Value);
                    break;
                case SqliteHostBindingType.Float64:
                    rc = NativeMethods.sqlite3_bind_double(statement, index, value.Float64Value);
                    break;
                default:
                    rc = NativeMethods.sqlite3_bind_null(statement, index);
                    break;
            }
            if (rc != NativeMethods.SQLITE_OK)
            {
                throw CreateError("sqlite3_bind", rc);
            }
        }

        /// <summary>
        /// Adapter contract: surface native failures with their SQLite
        /// result code. The extended error code is carried when the handle
        /// reports one (e.g. SQLITE_CONSTRAINT_PRIMARYKEY = 1555); its low
        /// byte is always the primary code.
        /// </summary>
        private SqliteHostAdapterException CreateError(string operation, int rc)
        {
            int code = NativeMethods.sqlite3_extended_errcode(_db);
            if (code == 0)
            {
                code = rc;
            }
            string message = NativeMethods.FromUtf8Z(NativeMethods.sqlite3_errmsg(_db));
            return new SqliteHostAdapterException(
                operation + " failed (" + code + "): " + message, code, null);
        }

        /// <summary>
        /// Row view over the statement's current row; valid only while the
        /// owning Query call is stepping (reads after that throw instead of
        /// touching a finalized statement).
        /// </summary>
        private sealed class NativeRow : ISqliteHostRow
        {
            private IntPtr _statement;

            public NativeRow(IntPtr statement)
            {
                _statement = statement;
            }

            public void Invalidate()
            {
                _statement = IntPtr.Zero;
            }

            private IntPtr Statement
            {
                get
                {
                    if (_statement == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "This row view is only valid inside the Query call that produced it.");
                    }
                    return _statement;
                }
            }

            public bool IsNull(int index)
                => NativeMethods.sqlite3_column_type(Statement, index) == NativeMethods.SQLITE_NULL;

            public int GetInt32(int index)
                => NativeMethods.sqlite3_column_int(Statement, index);

            public long GetInt64(int index)
                => NativeMethods.sqlite3_column_int64(Statement, index);

            public bool GetBool(int index)
                => NativeMethods.sqlite3_column_int64(Statement, index) != 0;

            public string GetText(int index)
            {
                // Per the C API docs, fetch the pointer first and the byte
                // count second (NULL columns read as null, like the other
                // adapters).
                IntPtr statement = Statement;
                IntPtr text = NativeMethods.sqlite3_column_text(statement, index);
                int byteCount = NativeMethods.sqlite3_column_bytes(statement, index);
                return NativeMethods.FromUtf8(text, byteCount);
            }

            public byte[] GetBlob(int index)
            {
                // Empty blob reads as an empty array, never null.
                IntPtr statement = Statement;
                IntPtr blob = NativeMethods.sqlite3_column_blob(statement, index);
                int byteCount = NativeMethods.sqlite3_column_bytes(statement, index);
                return NativeMethods.CopyBytes(blob, byteCount);
            }

            public float GetFloat32(int index)
                => (float)NativeMethods.sqlite3_column_double(Statement, index);

            public double GetFloat64(int index)
                => NativeMethods.sqlite3_column_double(Statement, index);
        }

        /// <summary>
        /// Compiled-never-stepped statement exposing parameter metadata.
        /// Names are raw as written in the SQL, prefix included (":id",
        /// "@id", "$id"), matching the other adapters; nameless positional
        /// parameters contribute a null entry.
        /// </summary>
        private sealed class NativePreparedStatement : ISqliteHostPreparedStatement
        {
            private readonly NativeSqliteHostConnection _owner;
            private readonly List<string> _parameterNames;
            private IntPtr _statement;

            public NativePreparedStatement(NativeSqliteHostConnection owner, IntPtr statement)
            {
                _owner = owner;
                _statement = statement;
                _parameterNames = new List<string>();
                int count = NativeMethods.sqlite3_bind_parameter_count(statement);
                for (int i = 1; i <= count; i++)
                {
                    _parameterNames.Add(NativeMethods.FromUtf8Z(
                        NativeMethods.sqlite3_bind_parameter_name(statement, i)));
                }
            }

            public IReadOnlyList<string> ParameterNames => _parameterNames;

            /// <summary>Finalizes the native statement once; safe to call repeatedly.</summary>
            public void FinalizeStatement()
            {
                if (_statement != IntPtr.Zero)
                {
                    NativeMethods.sqlite3_finalize(_statement);
                    _statement = IntPtr.Zero;
                }
            }

            public void Dispose()
            {
                FinalizeStatement();
                _owner.RemoveLiveStatement(this);
            }
        }
    }
}
