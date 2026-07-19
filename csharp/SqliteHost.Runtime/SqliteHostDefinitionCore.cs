using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Non-generic core behind <see cref="SqliteHostDefinition{THandlers}"/>:
    /// holds the erased method specs directly and carries all validation,
    /// feature computation, spec resolution, and workspace schema
    /// generation, so the generic definition type stays a thin API-typing
    /// wrapper.
    /// </summary>
    internal sealed class SqliteHostDefinitionCore
    {
        /// <summary>Applied when the builder's MinSqliteVersion is not called: SQLite 3.19.3.</summary>
        internal const int DefaultMinSqliteVersionNumber = 3019003;

        private static readonly IReadOnlyList<string> FeaturesV1 = new List<string>
        {
            "typedNamedBindings",
            "splitResultTables",
            "scriptInputs",
            "scriptVars",
            "scriptControl"
        };

        private readonly List<ErasedHostMethodSpec> _specs;
        private readonly Dictionary<string, ErasedHostMethodSpec> _specsByMethod;

        public SqliteHostDefinitionCore(
            int apiLevel,
            int minSqliteVersionNumber,
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            IReadOnlyList<object> methods)
        {
            if (naming == null)
            {
                throw new ArgumentNullException(nameof(naming));
            }
            if (columns == null)
            {
                throw new ArgumentNullException(nameof(columns));
            }
            if (methods == null)
            {
                throw new ArgumentNullException(nameof(methods));
            }
            ApiLevel = apiLevel;
            MinSqliteVersionNumber = minSqliteVersionNumber;
            Naming = naming;
            Columns = columns;
            _specs = new List<ErasedHostMethodSpec>();
            _specsByMethod = new Dictionary<string, ErasedHostMethodSpec>(StringComparer.Ordinal);
            foreach (object method in methods)
            {
                var carrier = method as ErasedSpecCarrier;
                if (carrier == null)
                {
                    throw new ArgumentException(
                        "Method specs must be built through HostMethod.For(...); foreign IHostMethodSpec implementations are not supported.",
                        nameof(methods));
                }
                ErasedHostMethodSpec spec = carrier.Spec;
                if (_specsByMethod.ContainsKey(spec.MethodName))
                {
                    throw new ArgumentException(
                        "Duplicate method name '" + spec.MethodName + "'.",
                        nameof(methods));
                }
                _specsByMethod.Add(spec.MethodName, spec);
                _specs.Add(spec);
            }
            ValidateWorkspaceTableNames(naming, _specs);
            ValidateDerivedTableNames(naming, _specs);
            ValidateColumnNames(naming, columns, _specs);
            ValidateFieldSqlNames(_specs);
            ValidateInlineFunctionNames(naming, _specs);
            HasInlineFunctions = ComputeHasInlineFunctions(_specs);
        }

        private static bool ComputeHasInlineFunctions(List<ErasedHostMethodSpec> specs)
        {
            foreach (ErasedHostMethodSpec spec in specs)
            {
                if (spec.InlineFunction != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Inline function naming (docs/naming.md): the FunctionPrefix must
        /// be non-empty, and inline function names must be mutually
        /// distinct, must not collide with any workspace or derived
        /// call/result/child table name, and must not collide with a
        /// SQLite built-in function name. Fails loud at definition build
        /// time.
        /// </summary>
        private static void ValidateInlineFunctionNames(
            SqliteHostNaming naming,
            List<ErasedHostMethodSpec> specs)
        {
#if !SQLITEHOST_SLIM
            if (string.IsNullOrEmpty(naming.FunctionPrefix))
            {
                throw new ArgumentException("Naming FunctionPrefix must be non-empty.", nameof(naming));
            }
            var tableNames = new HashSet<string>(StringComparer.Ordinal)
            {
                naming.QueueTable,
                naming.InputsTable,
                naming.VarsTable,
                naming.ControlTable
            };
            foreach (ErasedHostMethodSpec spec in specs)
            {
                SchemaMethodModel model = spec.SchemaModel;
                tableNames.Add(NamingDerivation.CallTable(naming, model.MethodName));
                tableNames.Add(NamingDerivation.ResultTable(naming, model.MethodName));
                foreach (SchemaListFieldModel listField in model.InputListFields)
                {
                    tableNames.Add(NamingDerivation.InputListTable(naming, model.MethodName, listField.SqlName));
                }
                foreach (SchemaListFieldModel listField in model.ResultListFields)
                {
                    tableNames.Add(NamingDerivation.ResultListTable(naming, model.MethodName, listField.SqlName));
                }
            }
            var functionNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ErasedHostMethodSpec spec in specs)
            {
                if (spec.InlineFunction == null)
                {
                    continue;
                }
                string functionName = spec.InlineFunction.FunctionName;
                if (SqliteBuiltinFunctions.Contains(functionName))
                {
                    throw new ArgumentException(
                        "Inline function name '" + functionName + "' of method '" + spec.MethodName
                        + "' collides with a SQLite built-in function name.",
                        "methods");
                }
                if (tableNames.Contains(functionName))
                {
                    throw new ArgumentException(
                        "Inline function name '" + functionName + "' of method '" + spec.MethodName
                        + "' collides with a workspace or derived table name.",
                        "methods");
                }
                if (!functionNames.Add(functionName))
                {
                    throw new ArgumentException(
                        "Inline function name '" + functionName + "' is used by more than one method.",
                        "methods");
                }
            }
#endif
        }

        /// <summary>
        /// Workspace table names (docs/naming.md): non-empty, mutually
        /// distinct, and no collision with any derived call/result/child
        /// table name. Fails loud at definition build time.
        /// </summary>
        private static void ValidateWorkspaceTableNames(
            SqliteHostNaming naming,
            List<ErasedHostMethodSpec> specs)
        {
#if !SQLITEHOST_SLIM
            if (string.IsNullOrEmpty(naming.QueueTable)
                || string.IsNullOrEmpty(naming.InputsTable)
                || string.IsNullOrEmpty(naming.VarsTable)
                || string.IsNullOrEmpty(naming.ControlTable))
            {
                throw new ArgumentException(
                    "Workspace table names (QueueTable, InputsTable, VarsTable, ControlTable) must be non-empty.",
                    nameof(naming));
            }
            var distinctTables = new HashSet<string>(StringComparer.Ordinal);
            if (!distinctTables.Add(naming.QueueTable)
                || !distinctTables.Add(naming.InputsTable)
                || !distinctTables.Add(naming.VarsTable)
                || !distinctTables.Add(naming.ControlTable))
            {
                throw new ArgumentException(
                    "Workspace table names (QueueTable, InputsTable, VarsTable, ControlTable) must be mutually distinct.",
                    nameof(naming));
            }
            var workspaceTables = new HashSet<string>(StringComparer.Ordinal)
            {
                naming.QueueTable,
                naming.InputsTable,
                naming.VarsTable,
                naming.ControlTable
            };
            foreach (ErasedHostMethodSpec spec in specs)
            {
                SchemaMethodModel model = spec.SchemaModel;
                var derivedTables = new List<string>();
                derivedTables.Add(NamingDerivation.CallTable(naming, model.MethodName));
                derivedTables.Add(NamingDerivation.ResultTable(naming, model.MethodName));
                foreach (SchemaListFieldModel listField in model.InputListFields)
                {
                    derivedTables.Add(NamingDerivation.InputListTable(naming, model.MethodName, listField.SqlName));
                }
                foreach (SchemaListFieldModel listField in model.ResultListFields)
                {
                    derivedTables.Add(NamingDerivation.ResultListTable(naming, model.MethodName, listField.SqlName));
                }
                foreach (string derivedTable in derivedTables)
                {
                    if (workspaceTables.Contains(derivedTable))
                    {
                        throw new ArgumentException(
                            "Workspace table name '" + derivedTable
                            + "' collides with a derived table name of method '" + model.MethodName + "'.",
                            nameof(naming));
                    }
                }
            }
#endif
        }

        /// <summary>
        /// Derived table names (docs/naming.md): every method's derived
        /// call/result/child table name must be claimed by exactly one
        /// method, mirroring the canonical TypeSpec duplicate-table-name
        /// diagnostic (codegen/core/src/validate.ts). Fails loud at
        /// definition build time.
        /// </summary>
        private static void ValidateDerivedTableNames(
            SqliteHostNaming naming,
            List<ErasedHostMethodSpec> specs)
        {
#if !SQLITEHOST_SLIM
            var claimedTables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ErasedHostMethodSpec spec in specs)
            {
                SchemaMethodModel model = spec.SchemaModel;
                var derivedTables = new List<string>();
                derivedTables.Add(NamingDerivation.CallTable(naming, model.MethodName));
                derivedTables.Add(NamingDerivation.ResultTable(naming, model.MethodName));
                foreach (SchemaListFieldModel listField in model.InputListFields)
                {
                    derivedTables.Add(NamingDerivation.InputListTable(naming, model.MethodName, listField.SqlName));
                }
                foreach (SchemaListFieldModel listField in model.ResultListFields)
                {
                    derivedTables.Add(NamingDerivation.ResultListTable(naming, model.MethodName, listField.SqlName));
                }
                foreach (string derivedTable in derivedTables)
                {
                    string claimedBy;
                    if (claimedTables.TryGetValue(derivedTable, out claimedBy))
                    {
                        throw new ArgumentException(
                            "Derived table name '" + derivedTable + "' of method '" + model.MethodName
                            + "' is already used by method '" + claimedBy
                            + "'; method or list field names collide after naming derivation.",
                            "methods");
                    }
                    claimedTables.Add(derivedTable, model.MethodName);
                }
            }
#endif
        }

        /// <summary>
        /// Shared column names and the done literal (docs/naming.md):
        /// non-empty, mutually distinct within each table, and the
        /// row-identity columns (CallId/ItemIndex/Status) must not collide
        /// with any derived input/result field column. Fails loud at
        /// definition build time.
        /// </summary>
        private static void ValidateColumnNames(
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            List<ErasedHostMethodSpec> specs)
        {
#if !SQLITEHOST_SLIM
            RequireNonEmpty(columns.CallId, "CallId");
            RequireNonEmpty(columns.ItemIndex, "ItemIndex");
            RequireNonEmpty(columns.Status, "Status");
            RequireNonEmpty(columns.DoneValue, "DoneValue");
            RequireNonEmpty(columns.QueueId, "QueueId");
            RequireNonEmpty(columns.Method, "Method");
            RequireNonEmpty(columns.Name, "Name");
            RequireNonEmpty(columns.ValueType, "ValueType");
            RequireNonEmpty(columns.IntValue, "IntValue");
            RequireNonEmpty(columns.RealValue, "RealValue");
            RequireNonEmpty(columns.TextValue, "TextValue");
            RequireNonEmpty(columns.BlobValue, "BlobValue");
            RequireNonEmpty(columns.Action, "Action");
            RequireNonEmpty(columns.Message, "Message");

            RequireDistinctWithinTable("queue",
                columns.QueueId, columns.CallId, columns.Method, columns.Status);
            RequireDistinctWithinTable("inputs/vars",
                columns.Name, columns.ValueType, columns.IntValue,
                columns.RealValue, columns.TextValue, columns.BlobValue);
            RequireDistinctWithinTable("control",
                columns.Action, columns.Message);

            var rowIdentityColumns = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { columns.CallId, "CallId" },
                { columns.ItemIndex, "ItemIndex" },
                { columns.Status, "Status" }
            };
            foreach (ErasedHostMethodSpec spec in specs)
            {
                SchemaMethodModel model = spec.SchemaModel;
                var derivedColumns = new List<string>();
                foreach (SchemaFieldModel field in model.InputFields)
                {
                    derivedColumns.Add(NamingDerivation.InputColumn(naming, field.SqlName));
                }
                foreach (SchemaListFieldModel listField in model.InputListFields)
                {
                    foreach (SchemaFieldModel field in listField.ItemFields)
                    {
                        derivedColumns.Add(NamingDerivation.InputColumn(naming, field.SqlName));
                    }
                }
                foreach (SchemaFieldModel field in model.ResultFields)
                {
                    derivedColumns.Add(NamingDerivation.ResultColumn(naming, field.SqlName));
                }
                foreach (SchemaListFieldModel listField in model.ResultListFields)
                {
                    foreach (SchemaFieldModel field in listField.ItemFields)
                    {
                        derivedColumns.Add(NamingDerivation.ResultColumn(naming, field.SqlName));
                    }
                }
                foreach (string derivedColumn in derivedColumns)
                {
                    string identityName;
                    if (rowIdentityColumns.TryGetValue(derivedColumn, out identityName))
                    {
                        throw new ArgumentException(
                            "Row-identity column " + identityName + " ('" + derivedColumn
                            + "') collides with a derived field column of method '" + model.MethodName + "'.",
                            "columns");
                    }
                }
            }
#endif
        }

        /// <summary>
        /// Field SQL names (docs/naming.md): SQL names must be mutually
        /// distinct within each input/result shape (scalar and list
        /// fields share the shape's namespace) and within each list item
        /// shape, mirroring the canonical TypeSpec duplicate-sql-name
        /// diagnostic (codegen/core/src/validate.ts). Fails loud at
        /// definition build time.
        /// </summary>
        private static void ValidateFieldSqlNames(List<ErasedHostMethodSpec> specs)
        {
#if !SQLITEHOST_SLIM
            foreach (ErasedHostMethodSpec spec in specs)
            {
                SchemaMethodModel model = spec.SchemaModel;
                RequireDistinctShapeSqlNames(
                    "input", model.MethodName, model.InputFields, model.InputListFields);
                RequireDistinctShapeSqlNames(
                    "result", model.MethodName, model.ResultFields, model.ResultListFields);
            }
#endif
        }
#if !SQLITEHOST_SLIM

        /// <summary>
        /// SQLite built-in scalar/aggregate function names an inline
        /// function name must not collide with (docs/naming.md). Mirrors
        /// SQLITE_BUILTIN_FUNCTIONS in codegen/core/src/validate.ts;
        /// SQLite resolves function names case-insensitively, so
        /// membership ignores case.
        /// </summary>
        private static readonly HashSet<string> SqliteBuiltinFunctions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "abs",
                "coalesce",
                "count",
                "sum",
                "min",
                "max",
                "length",
                "lower",
                "upper",
                "printf",
                "random",
                "replace",
                "round",
                "substr",
                "trim",
                "date",
                "time",
                "datetime",
                "ifnull",
                "nullif",
                "instr",
                "hex",
                "quote",
                "total",
                "group_concat",
                "typeof",
                "unicode",
                "char",
                "likelihood",
                "likely",
                "unlikely",
                "last_insert_rowid",
                "changes",
                "sqlite_version",
                "glob",
                "like",
                "zeroblob"
            };

        private static void RequireNonEmpty(string value, string columnProperty)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(
                    "Column name " + columnProperty + " (and the done literal) must be non-empty.",
                    "columns");
            }
        }

        private static void RequireDistinctWithinTable(string table, params string[] names)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (!seen.Add(name))
                {
                    throw new ArgumentException(
                        "Column name '" + name + "' occurs more than once in the " + table + " table.",
                        "columns");
                }
            }
        }

        private static void RequireDistinctShapeSqlNames(
            string shape,
            string methodName,
            IReadOnlyList<SchemaFieldModel> fields,
            IReadOnlyList<SchemaListFieldModel> listFields)
        {
            var sqlNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (SchemaFieldModel field in fields)
            {
                if (!sqlNames.Add(field.SqlName))
                {
                    throw new ArgumentException(
                        "SQL name '" + field.SqlName + "' occurs more than once in the "
                        + shape + " shape of method '" + methodName + "'.",
                        "methods");
                }
            }
            foreach (SchemaListFieldModel listField in listFields)
            {
                if (!sqlNames.Add(listField.SqlName))
                {
                    throw new ArgumentException(
                        "SQL name '" + listField.SqlName + "' occurs more than once in the "
                        + shape + " shape of method '" + methodName + "'.",
                        "methods");
                }
                var itemSqlNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (SchemaFieldModel itemField in listField.ItemFields)
                {
                    if (!itemSqlNames.Add(itemField.SqlName))
                    {
                        throw new ArgumentException(
                            "SQL name '" + itemField.SqlName + "' occurs more than once in the item shape of list field '"
                            + listField.SqlName + "' of method '" + methodName + "'.",
                            "methods");
                    }
                }
            }
        }
#endif

        public int ApiLevel { get; }

        /// <summary>
        /// Minimum accepted SQLite version in the SQLITE_VERSION_NUMBER
        /// encoding (major*1000000 + minor*1000 + patch), e.g. 3019003;
        /// defaults to 3019003 when the builder's MinSqliteVersion is not
        /// called. Enforced by the runtime's workspace version gate.
        /// </summary>
        public int MinSqliteVersionNumber { get; }

        public SqliteHostNaming Naming { get; }

        public SqliteHostColumns Columns { get; }

        /// <summary>True when at least one method is exposed as an inline scalar function.</summary>
        public bool HasInlineFunctions { get; }

        /// <summary>The registered specs' erased execution cores (inline metadata, schema models).</summary>
        public IReadOnlyList<ErasedHostMethodSpec> Specs
        {
            get { return _specs; }
        }

        /// <summary>Protocol v1 features: typedNamedBindings, splitResultTables, scriptInputs, scriptVars, scriptControl.</summary>
        public IReadOnlyList<string> SupportedFeatures
        {
            get { return FeaturesV1; }
        }

        public IReadOnlyList<string> GenerateSchemaStatements()
        {
            var models = new List<SchemaMethodModel>();
            foreach (ErasedHostMethodSpec spec in _specs)
            {
                models.Add(spec.SchemaModel);
            }
            return SchemaGenerator.GenerateStatements(Naming, Columns, models);
        }

        /// <summary>Full DDL script — byte-identical to the committed DDL snapshot fixture.</summary>
        public string GenerateSchemaScript()
        {
            var models = new List<SchemaMethodModel>();
            foreach (ErasedHostMethodSpec spec in _specs)
            {
                models.Add(spec.SchemaModel);
            }
            return SchemaGenerator.GenerateScript(Naming, Columns, models);
        }

        public ErasedHostMethodSpec ResolveSpec(string methodName)
        {
            ErasedHostMethodSpec spec;
            return methodName != null && _specsByMethod.TryGetValue(methodName, out spec) ? spec : null;
        }
    }
}
