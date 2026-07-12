namespace SqliteHost
{
    /// <summary>Fluent configuration of <see cref="SqliteHostNaming"/>; each setter returns this.</summary>
    public sealed class SqliteHostNamingBuilder
    {
        private string _callTablePrefix = "call_";
        private string _resultTablePrefix = "result_";
        private string _inputColumnPrefix = "input_";
        private string _resultColumnPrefix = "result_";
        private string _inputListTableInfix = "__input_";
        private string _resultListTableInfix = "__result_";
        private string _queueTable = "pending_host_calls";
        private string _inputsTable = "script_inputs";
        private string _varsTable = "script_vars";
        private string _controlTable = "script_control";
        private string _functionPrefix = "fn_";

        public SqliteHostNamingBuilder CallTablePrefix(string value)
        {
            _callTablePrefix = value;
            return this;
        }

        public SqliteHostNamingBuilder ResultTablePrefix(string value)
        {
            _resultTablePrefix = value;
            return this;
        }

        public SqliteHostNamingBuilder InputColumnPrefix(string value)
        {
            _inputColumnPrefix = value;
            return this;
        }

        public SqliteHostNamingBuilder ResultColumnPrefix(string value)
        {
            _resultColumnPrefix = value;
            return this;
        }

        public SqliteHostNamingBuilder InputListTableInfix(string value)
        {
            _inputListTableInfix = value;
            return this;
        }

        public SqliteHostNamingBuilder ResultListTableInfix(string value)
        {
            _resultListTableInfix = value;
            return this;
        }

        public SqliteHostNamingBuilder QueueTable(string value)
        {
            _queueTable = value;
            return this;
        }

        public SqliteHostNamingBuilder InputsTable(string value)
        {
            _inputsTable = value;
            return this;
        }

        public SqliteHostNamingBuilder VarsTable(string value)
        {
            _varsTable = value;
            return this;
        }

        public SqliteHostNamingBuilder ControlTable(string value)
        {
            _controlTable = value;
            return this;
        }

        public SqliteHostNamingBuilder FunctionPrefix(string value)
        {
            _functionPrefix = value;
            return this;
        }

        internal SqliteHostNaming Build()
        {
            return new SqliteHostNaming(
                _callTablePrefix,
                _resultTablePrefix,
                _inputColumnPrefix,
                _resultColumnPrefix,
                _inputListTableInfix,
                _resultListTableInfix,
                _queueTable,
                _inputsTable,
                _varsTable,
                _controlTable,
                _functionPrefix);
        }
    }
}
