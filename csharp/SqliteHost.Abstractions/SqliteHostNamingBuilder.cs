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

        internal SqliteHostNaming Build()
        {
            return new SqliteHostNaming(
                _callTablePrefix,
                _resultTablePrefix,
                _inputColumnPrefix,
                _resultColumnPrefix,
                _inputListTableInfix,
                _resultListTableInfix);
        }
    }
}
