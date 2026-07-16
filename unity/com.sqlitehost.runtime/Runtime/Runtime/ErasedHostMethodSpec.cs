using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// The type-erased execution core behind every method spec. One
    /// non-generic class carries the whole call lifecycle (read input row +
    /// child rows, invoke handler, write result row + child rows, inline
    /// function creation), so registering another method never mints new
    /// generic instantiations — the per-method AOT/IL2CPP cost is the DTO
    /// types and accessor delegates only. All three registration surfaces
    /// (<see cref="HostMethod"/>, <see cref="CompactHostMethod"/>,
    /// <see cref="UltraHostMethod"/>) lower to this class, which keeps
    /// their runtime behavior identical by construction.
    /// </summary>
    internal sealed class ErasedHostMethodSpec
    {
        private readonly Func<object> _createInput;
        private readonly IReadOnlyList<ErasedReadField> _inputFields;
        private readonly IReadOnlyList<ErasedInputListField> _inputListFields;
        private readonly IReadOnlyList<ErasedWriteField> _resultFields;
        private readonly IReadOnlyList<ErasedResultListField> _resultListFields;
        private readonly Func<object, object, object> _handler;

        public ErasedHostMethodSpec(
            string methodName,
            int apiLevel,
            Func<object> createInput,
            IReadOnlyList<ErasedReadField> inputFields,
            IReadOnlyList<ErasedInputListField> inputListFields,
            IReadOnlyList<ErasedWriteField> resultFields,
            IReadOnlyList<ErasedResultListField> resultListFields,
            Func<object, object, object> handler,
            InlineFunctionModel inlineFunction)
        {
            MethodName = methodName;
            ApiLevel = apiLevel;
            _createInput = createInput;
            _inputFields = inputFields;
            _inputListFields = inputListFields;
            _resultFields = resultFields;
            _resultListFields = resultListFields;
            _handler = handler;
            InlineFunction = inlineFunction;
            SchemaModel = BuildSchemaModel();
        }

        public string MethodName { get; }
        public int ApiLevel { get; }
        public SchemaMethodModel SchemaModel { get; }
        public InlineFunctionModel InlineFunction { get; }

        public SqliteHostScalarFunction CreateInlineFunction(object handlers, Action onHandlerInvocation)
        {
            InlineFunctionModel inline = InlineFunction;
            if (inline == null)
            {
                throw new InvalidOperationException(
                    "Method '" + MethodName + "' is not exposed as an inline function.");
            }
            IReadOnlyList<ErasedReadField> inputFields = _inputFields;
            ErasedWriteField resultField = _resultFields[0];
            Func<object, object, object> handler = _handler;
            Func<object> createInput = _createInput;
            return new SqliteHostScalarFunction(
                inline.FunctionName,
                inline.MinArgs,
                inline.MaxArgs,
                delegate(SqliteHostBindingValue[] args)
                {
                    try
                    {
                        object input = createInput();
                        var row = new InlineArgumentRow(args);
                        for (int i = 0; i < inputFields.Count; i++)
                        {
                            ErasedReadField field = inputFields[i];
                            if (row.IsNull(i))
                            {
                                if (!field.Optional)
                                {
                                    throw new SqliteHostInlineArgumentException(
                                        "Argument '" + field.SqlName + "' is required but received NULL.");
                                }
                                if (i >= args.Length)
                                {
                                    // Omitted trailing arg: the DTO property
                                    // keeps its default (= null).
                                    continue;
                                }
                            }
                            field.Apply(input, row, i);
                        }
                        if (onHandlerInvocation != null)
                        {
                            onHandlerInvocation();
                        }
                        object result = handler(handlers, input);
                        return resultField.Read(result);
                    }
                    catch (Exception ex)
                    {
                        throw new SqliteHostInlineFunctionException(inline.FunctionName, ex);
                    }
                });
        }

        public void ExecuteCall(
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            object handlers,
            string callId)
        {
            object input = ReadInput(connection, naming, columns, callId);

            object result;
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
                WriteResultParentRow(connection, naming, columns, callId, result);
                foreach (ErasedResultListField listField in _resultListFields)
                {
                    WriteResultListRows(listField, result, connection, naming, columns, callId);
                }
            }
            catch (Exception ex)
            {
                throw new SqliteHostResultWriteException(ex);
            }
        }

        private object ReadInput(
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            SqliteHostColumns hostColumns,
            string callId)
        {
            string callTable = NamingDerivation.CallTable(naming, MethodName);
            var columns = new List<string>();
            foreach (ErasedReadField field in _inputFields)
            {
                columns.Add(NamingDerivation.InputColumn(naming, field.SqlName));
            }
            string selectList = columns.Count > 0 ? string.Join(", ", columns) : hostColumns.CallId;
            string sql = "SELECT " + selectList + " FROM " + callTable
                + " WHERE " + hostColumns.CallId + " = :callId";

            IReadOnlyList<ErasedReadField> inputFields = _inputFields;
            Func<object> createInput = _createInput;
            IReadOnlyList<object> rows = connection.Query(
                sql,
                RuntimeSql.CallIdBindings(callId),
                delegate(ISqliteHostRow row)
                {
                    object dto = createInput();
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

            object input = rows[0];
            foreach (ErasedInputListField listField in _inputListFields)
            {
                LoadInputListRows(listField, input, connection, naming, hostColumns, callId);
            }
            return input;
        }

        private void LoadInputListRows(
            ErasedInputListField listField,
            object input,
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            SqliteHostColumns hostColumns,
            string callId)
        {
            string childTable = NamingDerivation.InputListTable(naming, MethodName, listField.SqlName);
            var columns = new List<string>();
            IReadOnlyList<ErasedReadField> itemFields = listField.ItemFields;
            foreach (ErasedReadField field in itemFields)
            {
                columns.Add(NamingDerivation.InputColumn(naming, field.SqlName));
            }
            string sql = "SELECT " + string.Join(", ", columns)
                + " FROM " + childTable
                + " WHERE " + hostColumns.CallId + " = :callId ORDER BY " + hostColumns.ItemIndex;
            Func<object> createItem = listField.CreateItem;
            IReadOnlyList<object> items = connection.Query(
                sql,
                RuntimeSql.CallIdBindings(callId),
                delegate(ISqliteHostRow row)
                {
                    object item = createItem();
                    for (int i = 0; i < itemFields.Count; i++)
                    {
                        itemFields[i].Apply(item, row, i);
                    }
                    return item;
                });
            listField.AssignItems(input, items);
        }

        private void WriteResultParentRow(
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            SqliteHostColumns hostColumns,
            string callId,
            object result)
        {
            string resultTable = NamingDerivation.ResultTable(naming, MethodName);
            var columns = new List<string> { hostColumns.CallId, hostColumns.Status };
            var placeholders = new List<string> { ":callId", ":status" };
            var bindings = new List<SqliteHostBinding>
            {
                new SqliteHostBinding("callId", SqliteHostBindingValue.Text(callId)),
                new SqliteHostBinding("status", SqliteHostBindingValue.Text(hostColumns.DoneValue))
            };
            for (int i = 0; i < _resultFields.Count; i++)
            {
                ErasedWriteField field = _resultFields[i];
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

        private void WriteResultListRows(
            ErasedResultListField listField,
            object result,
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            SqliteHostColumns hostColumns,
            string callId)
        {
            IReadOnlyList<object> items = listField.GetItems(result);
            if (items == null || items.Count == 0)
            {
                return;
            }
            string childTable = NamingDerivation.ResultListTable(naming, MethodName, listField.SqlName);
            IReadOnlyList<ErasedWriteField> itemFields = listField.ItemFields;
            var columns = new List<string> { hostColumns.CallId, hostColumns.ItemIndex };
            var placeholders = new List<string> { ":callId", ":itemIndex" };
            for (int i = 0; i < itemFields.Count; i++)
            {
                columns.Add(NamingDerivation.ResultColumn(naming, itemFields[i].SqlName));
                placeholders.Add(":v" + i);
            }
            string sql = "INSERT INTO " + childTable
                + " (" + string.Join(", ", columns) + ")"
                + " VALUES (" + string.Join(", ", placeholders) + ")";
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var bindings = new List<SqliteHostBinding>
                {
                    new SqliteHostBinding("callId", SqliteHostBindingValue.Text(callId)),
                    new SqliteHostBinding("itemIndex", SqliteHostBindingValue.Int32(itemIndex))
                };
                for (int i = 0; i < itemFields.Count; i++)
                {
                    bindings.Add(new SqliteHostBinding("v" + i, itemFields[i].Read(items[itemIndex])));
                }
                connection.Execute(sql, bindings);
            }
        }

        private SchemaMethodModel BuildSchemaModel()
        {
            var inputFields = new List<SchemaFieldModel>();
            foreach (ErasedReadField field in _inputFields)
            {
                inputFields.Add(field.ToSchemaField());
            }
            var inputListFields = new List<SchemaListFieldModel>();
            foreach (ErasedInputListField listField in _inputListFields)
            {
                inputListFields.Add(new SchemaListFieldModel(listField.SqlName, listField.ItemSchemaFields));
            }
            var resultFields = new List<SchemaFieldModel>();
            foreach (ErasedWriteField field in _resultFields)
            {
                resultFields.Add(field.ToSchemaField());
            }
            var resultListFields = new List<SchemaListFieldModel>();
            foreach (ErasedResultListField listField in _resultListFields)
            {
                resultListFields.Add(new SchemaListFieldModel(listField.SqlName, listField.ItemSchemaFields));
            }
            return new SchemaMethodModel(MethodName, inputFields, inputListFields, resultFields, resultListFields);
        }
    }

    /// <summary>
    /// Binds an <see cref="ErasedHostMethodSpec"/> to the typed
    /// <see cref="IRuntimeHostMethodSpec{THandlers}"/> contract the runtime
    /// consumes. One generic instantiation per handlers type — shared by
    /// every method of the host, never per method.
    /// </summary>
    internal sealed class ErasedSpecAdapter<THandlers> : IRuntimeHostMethodSpec<THandlers>
    {
        private readonly ErasedHostMethodSpec _spec;

        public ErasedSpecAdapter(ErasedHostMethodSpec spec)
        {
            _spec = spec;
        }

        public string MethodName
        {
            get { return _spec.MethodName; }
        }

        public int ApiLevel
        {
            get { return _spec.ApiLevel; }
        }

        public SchemaMethodModel SchemaModel
        {
            get { return _spec.SchemaModel; }
        }

        public InlineFunctionModel InlineFunction
        {
            get { return _spec.InlineFunction; }
        }

        public SqliteHostScalarFunction CreateInlineFunction(THandlers handlers, Action onHandlerInvocation)
        {
            return _spec.CreateInlineFunction(handlers, onHandlerInvocation);
        }

        public void ExecuteCall(
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            THandlers handlers,
            string callId)
        {
            _spec.ExecuteCall(connection, naming, columns, handlers, callId);
        }
    }
}
