namespace SqliteHost
{
    /// <summary>
    /// Optional capability: a connection that can register scalar functions
    /// (sqlite3_create_function). Adapter contract
    /// (docs/adapter-contract.md): register the function for every arity in
    /// MinArgs..MaxArgs, catch EVERYTHING thrown by Invoke and report it
    /// through the SQL error channel prefixed with
    /// <see cref="SqliteHostScalarFunction.HandlerErrorMarker"/> — an
    /// exception must never cross the native frames (IL2CPP safety) — and
    /// do not register with SQLITE_DETERMINISTIC (v1 rule).
    /// </summary>
    public interface ISqliteHostScalarFunctionConnection : ISqliteHostConnection
    {
        void RegisterScalarFunction(SqliteHostScalarFunction function);
    }
}
