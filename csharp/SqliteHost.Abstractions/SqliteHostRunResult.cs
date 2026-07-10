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

        /// <summary>Successfully completed handler invocations.</summary>
        public int ExecutedCallCount { get; set; }

        /// <summary>Populated when <see cref="SqliteHostRuntimeOptions"/> EnableDiagnostics is on.</summary>
        public List<SqliteHostCallDiagnostic> Calls { get; set; }
    }
}
