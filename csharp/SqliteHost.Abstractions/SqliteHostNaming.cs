namespace SqliteHost
{
    /// <summary>
    /// Host-level naming conventions (docs/naming.md). Physical table and
    /// column names belong to the host definition; method specs only carry
    /// logical snake_case names.
    /// </summary>
    public sealed class SqliteHostNaming
    {
        private static readonly SqliteHostNaming DefaultInstance = new SqliteHostNaming(
            "call_", "result_", "input_", "result_", "__input_", "__result_",
            "pending_host_calls", "script_inputs", "script_vars", "script_control", "fn_");

        internal SqliteHostNaming(
            string callTablePrefix,
            string resultTablePrefix,
            string inputColumnPrefix,
            string resultColumnPrefix,
            string inputListTableInfix,
            string resultListTableInfix,
            string queueTable,
            string inputsTable,
            string varsTable,
            string controlTable,
            string functionPrefix)
        {
            CallTablePrefix = callTablePrefix;
            ResultTablePrefix = resultTablePrefix;
            InputColumnPrefix = inputColumnPrefix;
            ResultColumnPrefix = resultColumnPrefix;
            InputListTableInfix = inputListTableInfix;
            ResultListTableInfix = resultListTableInfix;
            QueueTable = queueTable;
            InputsTable = inputsTable;
            VarsTable = varsTable;
            ControlTable = controlTable;
            FunctionPrefix = functionPrefix;
        }

        /// <summary>Protocol v1 defaults: call_ / result_ / input_ / result_ / __input_ / __result_.</summary>
        public static SqliteHostNaming Default
        {
            get { return DefaultInstance; }
        }

        public string CallTablePrefix { get; }
        public string ResultTablePrefix { get; }
        public string InputColumnPrefix { get; }
        public string ResultColumnPrefix { get; }
        public string InputListTableInfix { get; }
        public string ResultListTableInfix { get; }

        /// <summary>Pending-call queue table; default "pending_host_calls".</summary>
        public string QueueTable { get; }

        /// <summary>Runtime inputs table; default "script_inputs".</summary>
        public string InputsTable { get; }

        /// <summary>Script scratch variable table; default "script_vars".</summary>
        public string VarsTable { get; }

        /// <summary>Script early-exit control table; default "script_control".</summary>
        public string ControlTable { get; }

        /// <summary>Inline scalar-function name prefix; default "fn_".</summary>
        public string FunctionPrefix { get; }
    }
}
