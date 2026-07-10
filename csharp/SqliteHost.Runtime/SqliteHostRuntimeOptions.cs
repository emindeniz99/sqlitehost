namespace SqliteHost
{
    /// <summary>Runtime limits, validation toggles, and diagnostics settings.</summary>
    public sealed class SqliteHostRuntimeOptions
    {
        public SqliteHostRuntimeOptions()
        {
            ValidateBindings = true;
            EnableDiagnostics = false;
            MaxStatementsPerRun = 256;
            MaxPendingCallsPerStep = 64;
        }

        public bool ValidateBindings { get; set; }
        public bool EnableDiagnostics { get; set; }
        public int MaxStatementsPerRun { get; set; }
        public int MaxPendingCallsPerStep { get; set; }
    }
}
