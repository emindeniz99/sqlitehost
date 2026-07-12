using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>Entry point for building a <see cref="SqliteHostDefinition{THandlers}"/>.</summary>
    public static class SqliteHostDefinition
    {
        public static ISqliteHostDefinitionBuilder<THandlers> ForHandlers<THandlers>()
        {
            return new SqliteHostDefinitionBuilder<THandlers>();
        }
    }

    /// <summary>Fluent host definition builder (plan §11).</summary>
    public interface ISqliteHostDefinitionBuilder<THandlers>
    {
        ISqliteHostDefinitionBuilder<THandlers> ApiLevel(int apiLevel);
        ISqliteHostDefinitionBuilder<THandlers> MinSqliteVersion(int versionNumber);
        ISqliteHostDefinitionBuilder<THandlers> Naming(Action<SqliteHostNamingBuilder> configure);
        ISqliteHostDefinitionBuilder<THandlers> Columns(Action<SqliteHostColumnsBuilder> configure);
        SqliteHostDefinition<THandlers> Methods(IReadOnlyList<IHostMethodSpec<THandlers>> methods);
    }

    internal sealed class SqliteHostDefinitionBuilder<THandlers> : ISqliteHostDefinitionBuilder<THandlers>
    {
        private readonly SqliteHostNamingBuilder _naming = new SqliteHostNamingBuilder();
        private readonly SqliteHostColumnsBuilder _columns = new SqliteHostColumnsBuilder();
        private int _apiLevel = 1;
        private int _minSqliteVersionNumber = SqliteHostDefinition<THandlers>.DefaultMinSqliteVersionNumber;

        public ISqliteHostDefinitionBuilder<THandlers> ApiLevel(int apiLevel)
        {
            _apiLevel = apiLevel;
            return this;
        }

        public ISqliteHostDefinitionBuilder<THandlers> MinSqliteVersion(int versionNumber)
        {
            _minSqliteVersionNumber = versionNumber;
            return this;
        }

        public ISqliteHostDefinitionBuilder<THandlers> Naming(Action<SqliteHostNamingBuilder> configure)
        {
            configure(_naming);
            return this;
        }

        public ISqliteHostDefinitionBuilder<THandlers> Columns(Action<SqliteHostColumnsBuilder> configure)
        {
            configure(_columns);
            return this;
        }

        public SqliteHostDefinition<THandlers> Methods(IReadOnlyList<IHostMethodSpec<THandlers>> methods)
        {
            return new SqliteHostDefinition<THandlers>(
                _apiLevel, _minSqliteVersionNumber, _naming.Build(), _columns.Build(), methods);
        }
    }

    /// <summary>
    /// The host definition: API level, naming conventions, registered
    /// method specs, supported features, and workspace schema generation.
    /// </summary>
    public sealed class SqliteHostDefinition<THandlers>
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

        private readonly List<IRuntimeHostMethodSpec<THandlers>> _runtimeSpecs;
        private readonly Dictionary<string, IRuntimeHostMethodSpec<THandlers>> _specsByMethod;
        private readonly List<IHostMethodSpec<THandlers>> _methods;

        internal SqliteHostDefinition(
            int apiLevel,
            int minSqliteVersionNumber,
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            IReadOnlyList<IHostMethodSpec<THandlers>> methods)
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
            _runtimeSpecs = new List<IRuntimeHostMethodSpec<THandlers>>();
            _specsByMethod = new Dictionary<string, IRuntimeHostMethodSpec<THandlers>>(StringComparer.Ordinal);
            _methods = new List<IHostMethodSpec<THandlers>>();
            foreach (IHostMethodSpec<THandlers> method in methods)
            {
                var runtimeSpec = method as IRuntimeHostMethodSpec<THandlers>;
                if (runtimeSpec == null)
                {
                    throw new ArgumentException(
                        "Method specs must be built through HostMethod.For(...); foreign IHostMethodSpec implementations are not supported.",
                        nameof(methods));
                }
                if (_specsByMethod.ContainsKey(runtimeSpec.MethodName))
                {
                    throw new ArgumentException(
                        "Duplicate method name '" + runtimeSpec.MethodName + "'.",
                        nameof(methods));
                }
                _specsByMethod.Add(runtimeSpec.MethodName, runtimeSpec);
                _runtimeSpecs.Add(runtimeSpec);
                _methods.Add(method);
            }
            ValidateWorkspaceTableNames(naming, _runtimeSpecs);
            ValidateColumnNames(naming, columns, _runtimeSpecs);
            ValidateInlineFunctionNames(naming, _runtimeSpecs);
            HasInlineFunctions = ComputeHasInlineFunctions(_runtimeSpecs);
        }

        private static bool ComputeHasInlineFunctions(List<IRuntimeHostMethodSpec<THandlers>> specs)
        {
            foreach (IRuntimeHostMethodSpec<THandlers> spec in specs)
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
        /// distinct and must not collide with any workspace or derived
        /// call/result/child table name. Fails loud at definition build
        /// time.
        /// </summary>
        private static void ValidateInlineFunctionNames(
            SqliteHostNaming naming,
            List<IRuntimeHostMethodSpec<THandlers>> specs)
        {
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
            foreach (IRuntimeHostMethodSpec<THandlers> spec in specs)
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
            foreach (IRuntimeHostMethodSpec<THandlers> spec in specs)
            {
                if (spec.InlineFunction == null)
                {
                    continue;
                }
                string functionName = spec.InlineFunction.FunctionName;
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
        }

        /// <summary>
        /// Workspace table names (docs/naming.md): non-empty, mutually
        /// distinct, and no collision with any derived call/result/child
        /// table name. Fails loud at definition build time.
        /// </summary>
        private static void ValidateWorkspaceTableNames(
            SqliteHostNaming naming,
            List<IRuntimeHostMethodSpec<THandlers>> specs)
        {
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
            foreach (IRuntimeHostMethodSpec<THandlers> spec in specs)
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
            List<IRuntimeHostMethodSpec<THandlers>> specs)
        {
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
            foreach (IRuntimeHostMethodSpec<THandlers> spec in specs)
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
        }

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

        public IReadOnlyList<IHostMethodSpec<THandlers>> Methods
        {
            get { return _methods; }
        }

        /// <summary>True when at least one method is exposed as an inline scalar function.</summary>
        internal bool HasInlineFunctions { get; }

        /// <summary>The registered specs with their runtime-internal surface (inline metadata, schema models).</summary>
        internal IReadOnlyList<IRuntimeHostMethodSpec<THandlers>> RuntimeSpecs
        {
            get { return _runtimeSpecs; }
        }

        /// <summary>Protocol v1 features: typedNamedBindings, splitResultTables, scriptInputs, scriptVars, scriptControl.</summary>
        public IReadOnlyList<string> SupportedFeatures
        {
            get { return FeaturesV1; }
        }

        public IReadOnlyList<string> GenerateSchemaStatements()
        {
            var models = new List<SchemaMethodModel>();
            foreach (IRuntimeHostMethodSpec<THandlers> spec in _runtimeSpecs)
            {
                models.Add(spec.SchemaModel);
            }
            return SchemaGenerator.GenerateStatements(Naming, Columns, models);
        }

        /// <summary>Full DDL script — byte-identical to the committed DDL snapshot fixture.</summary>
        public string GenerateSchemaScript()
        {
            var models = new List<SchemaMethodModel>();
            foreach (IRuntimeHostMethodSpec<THandlers> spec in _runtimeSpecs)
            {
                models.Add(spec.SchemaModel);
            }
            return SchemaGenerator.GenerateScript(Naming, Columns, models);
        }

        internal IRuntimeHostMethodSpec<THandlers> ResolveSpec(string methodName)
        {
            IRuntimeHostMethodSpec<THandlers> spec;
            return methodName != null && _specsByMethod.TryGetValue(methodName, out spec) ? spec : null;
        }
    }
}
