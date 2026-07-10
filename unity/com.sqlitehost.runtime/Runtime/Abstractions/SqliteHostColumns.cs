namespace SqliteHost
{
    /// <summary>
    /// Host-level shared column names and the done-status literal
    /// (docs/naming.md): every remaining SQL-visible identifier of the
    /// runtime-managed tables. All values default to the protocol names.
    /// The control-table action verbs 'halt'/'fail' are protocol constants
    /// and deliberately not configurable.
    /// </summary>
    public sealed class SqliteHostColumns
    {
        private static readonly SqliteHostColumns DefaultInstance = new SqliteHostColumns(
            "call_id", "item_index", "status", "done",
            "queue_id", "method", "name", "value_type",
            "int_value", "real_value", "text_value", "blob_value",
            "action", "message");

        internal SqliteHostColumns(
            string callId,
            string itemIndex,
            string status,
            string doneValue,
            string queueId,
            string method,
            string name,
            string valueType,
            string intValue,
            string realValue,
            string textValue,
            string blobValue,
            string action,
            string message)
        {
            CallId = callId;
            ItemIndex = itemIndex;
            Status = status;
            DoneValue = doneValue;
            QueueId = queueId;
            Method = method;
            Name = name;
            ValueType = valueType;
            IntValue = intValue;
            RealValue = realValue;
            TextValue = textValue;
            BlobValue = blobValue;
            Action = action;
            Message = message;
        }

        /// <summary>Protocol defaults: call_id / item_index / status / done / queue_id / method / name / value_type / int_value / real_value / text_value / blob_value / action / message.</summary>
        public static SqliteHostColumns Default
        {
            get { return DefaultInstance; }
        }

        /// <summary>Call-identity column of every parent, child, and queue row; default "call_id".</summary>
        public string CallId { get; }

        /// <summary>Ordering column of list child tables; default "item_index".</summary>
        public string ItemIndex { get; }

        /// <summary>Status column of the queue and result tables; default "status".</summary>
        public string Status { get; }

        /// <summary>Status literal written for completed rows; default "done".</summary>
        public string DoneValue { get; }

        /// <summary>Autoincrement key of the queue table; default "queue_id".</summary>
        public string QueueId { get; }

        /// <summary>Method-name column of the queue table; default "method".</summary>
        public string Method { get; }

        /// <summary>Key column of the inputs/vars tables; default "name".</summary>
        public string Name { get; }

        /// <summary>Declared-type column of the inputs/vars tables; default "value_type".</summary>
        public string ValueType { get; }

        /// <summary>INTEGER value column of the inputs/vars tables; default "int_value".</summary>
        public string IntValue { get; }

        /// <summary>REAL value column of the inputs/vars tables; default "real_value".</summary>
        public string RealValue { get; }

        /// <summary>TEXT value column of the inputs/vars tables; default "text_value".</summary>
        public string TextValue { get; }

        /// <summary>BLOB value column of the inputs/vars tables; default "blob_value".</summary>
        public string BlobValue { get; }

        /// <summary>Action column of the control table; default "action".</summary>
        public string Action { get; }

        /// <summary>Message column of the control table; default "message".</summary>
        public string Message { get; }
    }
}
