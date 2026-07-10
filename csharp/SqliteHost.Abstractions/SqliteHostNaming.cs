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
            "call_", "result_", "input_", "result_", "__input_", "__result_");

        internal SqliteHostNaming(
            string callTablePrefix,
            string resultTablePrefix,
            string inputColumnPrefix,
            string resultColumnPrefix,
            string inputListTableInfix,
            string resultListTableInfix)
        {
            CallTablePrefix = callTablePrefix;
            ResultTablePrefix = resultTablePrefix;
            InputColumnPrefix = inputColumnPrefix;
            ResultColumnPrefix = resultColumnPrefix;
            InputListTableInfix = inputListTableInfix;
            ResultListTableInfix = resultListTableInfix;
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
    }
}
