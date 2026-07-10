namespace SqliteHost
{
    /// <summary>Fluent configuration of <see cref="SqliteHostColumns"/>; each setter returns this.</summary>
    public sealed class SqliteHostColumnsBuilder
    {
        private string _callId = "call_id";
        private string _itemIndex = "item_index";
        private string _status = "status";
        private string _doneValue = "done";
        private string _queueId = "queue_id";
        private string _method = "method";
        private string _name = "name";
        private string _valueType = "value_type";
        private string _intValue = "int_value";
        private string _realValue = "real_value";
        private string _textValue = "text_value";
        private string _blobValue = "blob_value";
        private string _action = "action";
        private string _message = "message";

        public SqliteHostColumnsBuilder CallId(string value)
        {
            _callId = value;
            return this;
        }

        public SqliteHostColumnsBuilder ItemIndex(string value)
        {
            _itemIndex = value;
            return this;
        }

        public SqliteHostColumnsBuilder Status(string value)
        {
            _status = value;
            return this;
        }

        public SqliteHostColumnsBuilder DoneValue(string value)
        {
            _doneValue = value;
            return this;
        }

        public SqliteHostColumnsBuilder QueueId(string value)
        {
            _queueId = value;
            return this;
        }

        public SqliteHostColumnsBuilder Method(string value)
        {
            _method = value;
            return this;
        }

        public SqliteHostColumnsBuilder Name(string value)
        {
            _name = value;
            return this;
        }

        public SqliteHostColumnsBuilder ValueType(string value)
        {
            _valueType = value;
            return this;
        }

        public SqliteHostColumnsBuilder IntValue(string value)
        {
            _intValue = value;
            return this;
        }

        public SqliteHostColumnsBuilder RealValue(string value)
        {
            _realValue = value;
            return this;
        }

        public SqliteHostColumnsBuilder TextValue(string value)
        {
            _textValue = value;
            return this;
        }

        public SqliteHostColumnsBuilder BlobValue(string value)
        {
            _blobValue = value;
            return this;
        }

        public SqliteHostColumnsBuilder Action(string value)
        {
            _action = value;
            return this;
        }

        public SqliteHostColumnsBuilder Message(string value)
        {
            _message = value;
            return this;
        }

        internal SqliteHostColumns Build()
        {
            return new SqliteHostColumns(
                _callId,
                _itemIndex,
                _status,
                _doneValue,
                _queueId,
                _method,
                _name,
                _valueType,
                _intValue,
                _realValue,
                _textValue,
                _blobValue,
                _action,
                _message);
        }
    }
}
