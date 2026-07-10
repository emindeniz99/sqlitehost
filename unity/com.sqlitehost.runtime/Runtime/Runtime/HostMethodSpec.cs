using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>Concrete method spec produced by the fluent descriptor API.</summary>
    internal sealed class HostMethodSpec<THandlers, TInput, TResult> : IRuntimeHostMethodSpec<THandlers>
        where TInput : new()
        where TResult : class
    {
        private readonly List<ScalarReadField<TInput>> _inputFields;
        private readonly List<InputListField<TInput>> _inputListFields;
        private readonly List<ScalarWriteField<TResult>> _resultFields;
        private readonly List<ResultListField<TResult>> _resultListFields;
        private readonly Func<THandlers, TInput, TResult> _handler;

        public HostMethodSpec(
            string methodName,
            int apiLevel,
            List<ScalarReadField<TInput>> inputFields,
            List<InputListField<TInput>> inputListFields,
            List<ScalarWriteField<TResult>> resultFields,
            List<ResultListField<TResult>> resultListFields,
            Func<THandlers, TInput, TResult> handler)
        {
            MethodName = methodName;
            ApiLevel = apiLevel;
            _inputFields = inputFields;
            _inputListFields = inputListFields;
            _resultFields = resultFields;
            _resultListFields = resultListFields;
            _handler = handler;
            SchemaModel = BuildSchemaModel();
        }

        public string MethodName { get; }
        public int ApiLevel { get; }
        public SchemaMethodModel SchemaModel { get; }

        public void ExecuteCall(
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            THandlers handlers,
            string callId)
        {
            TInput input = ReadInput(connection, naming, callId);

            TResult result;
            try
            {
                result = _handler(handlers, input);
            }
            catch (Exception ex)
            {
                throw new SqliteHostHandlerException(ex);
            }

            try
            {
                WriteResultParentRow(connection, naming, callId, result);
                foreach (ResultListField<TResult> listField in _resultListFields)
                {
                    listField.Write(result, connection, naming, MethodName, callId);
                }
            }
            catch (Exception ex)
            {
                throw new SqliteHostResultWriteException(ex);
            }
        }

        private TInput ReadInput(ISqliteHostConnection connection, SqliteHostNaming naming, string callId)
        {
            string callTable = NamingDerivation.CallTable(naming, MethodName);
            var columns = new List<string>();
            foreach (ScalarReadField<TInput> field in _inputFields)
            {
                columns.Add(NamingDerivation.InputColumn(naming, field.SqlName));
            }
            string selectList = columns.Count > 0 ? string.Join(", ", columns) : "call_id";
            string sql = "SELECT " + selectList + " FROM " + callTable + " WHERE call_id = :callId";

            List<ScalarReadField<TInput>> inputFields = _inputFields;
            IReadOnlyList<TInput> rows = connection.Query(
                sql,
                RuntimeSql.CallIdBindings(callId),
                delegate(ISqliteHostRow row)
                {
                    var dto = new TInput();
                    for (int i = 0; i < inputFields.Count; i++)
                    {
                        inputFields[i].Apply(dto, row, i);
                    }
                    return dto;
                });

            if (rows.Count == 0)
            {
                throw new SqliteHostCallRowMissingException(
                    "Call row '" + callId + "' is missing from " + callTable + ".");
            }

            TInput input = rows[0];
            foreach (InputListField<TInput> listField in _inputListFields)
            {
                listField.Load(input, connection, naming, MethodName, callId);
            }
            return input;
        }

        private void WriteResultParentRow(
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            string callId,
            TResult result)
        {
            string resultTable = NamingDerivation.ResultTable(naming, MethodName);
            var columns = new List<string> { "call_id", "status" };
            var placeholders = new List<string> { ":callId", ":status" };
            var bindings = new List<SqliteHostBinding>
            {
                new SqliteHostBinding("callId", SqliteHostBindingValue.Text(callId)),
                new SqliteHostBinding("status", SqliteHostBindingValue.Text("done"))
            };
            for (int i = 0; i < _resultFields.Count; i++)
            {
                ScalarWriteField<TResult> field = _resultFields[i];
                string parameter = "r" + i;
                columns.Add(NamingDerivation.ResultColumn(naming, field.SqlName));
                placeholders.Add(":" + parameter);
                bindings.Add(new SqliteHostBinding(parameter, field.Read(result)));
            }
            string sql = "INSERT INTO " + resultTable
                + " (" + string.Join(", ", columns) + ")"
                + " VALUES (" + string.Join(", ", placeholders) + ")";
            connection.Execute(sql, bindings);
        }

        private SchemaMethodModel BuildSchemaModel()
        {
            var inputFields = new List<SchemaFieldModel>();
            foreach (ScalarReadField<TInput> field in _inputFields)
            {
                inputFields.Add(field.ToSchemaField());
            }
            var inputListFields = new List<SchemaListFieldModel>();
            foreach (InputListField<TInput> listField in _inputListFields)
            {
                inputListFields.Add(new SchemaListFieldModel(listField.SqlName, listField.ItemSchemaFields));
            }
            var resultFields = new List<SchemaFieldModel>();
            foreach (ScalarWriteField<TResult> field in _resultFields)
            {
                resultFields.Add(field.ToSchemaField());
            }
            var resultListFields = new List<SchemaListFieldModel>();
            foreach (ResultListField<TResult> listField in _resultListFields)
            {
                resultListFields.Add(new SchemaListFieldModel(listField.SqlName, listField.ItemSchemaFields));
            }
            return new SchemaMethodModel(MethodName, inputFields, inputListFields, resultFields, resultListFields);
        }
    }
}
