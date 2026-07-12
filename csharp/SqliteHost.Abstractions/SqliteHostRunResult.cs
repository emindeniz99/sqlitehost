using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Structured run result. The runtime never throws for script-level
    /// problems; the host application decides logging/telemetry policy.
    /// </summary>
    public sealed class SqliteHostRunResult
    {
        public SqliteHostRunStatus Status { get; set; }

        /// <summary>Stable error code (docs/errors.md); null when Completed.</summary>
        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public string StepId { get; set; }

        /// <summary>-1 when the failure is not statement-scoped.</summary>
        public int StatementIndex { get; set; }

        /// <summary>Set for call-scoped failures.</summary>
        public string Method { get; set; }

        /// <summary>Set for missing-binding/unused-binding failures.</summary>
        public string BindingName { get; set; }

        /// <summary>
        /// Native SQLite error code when the adapter surfaced one via
        /// <see cref="SqliteHostAdapterException"/>; 0 = not available.
        /// </summary>
        public int SqliteErrorCode { get; set; }

        /// <summary>True when the script halted itself via the control table (Status stays Completed).</summary>
        public bool Halted { get; set; }

        /// <summary>The script's optional halt message.</summary>
        public string HaltMessage { get; set; }

        /// <summary>Successfully completed handler invocations.</summary>
        public int ExecutedCallCount { get; set; }

        /// <summary>
        /// Handler invocations made through inline scalar functions
        /// (informational — the SQLite planner may evaluate a function
        /// 0..N times per row).
        /// </summary>
        public int InlineCallCount { get; set; }

        /// <summary>Populated when <see cref="SqliteHostRuntimeOptions"/> EnableDiagnostics is on.</summary>
        public List<SqliteHostCallDiagnostic> Calls { get; set; }
    }
}
