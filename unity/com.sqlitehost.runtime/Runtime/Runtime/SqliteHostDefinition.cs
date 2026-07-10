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
        SqliteHostDefinition<THandlers> Methods(IReadOnlyList<IHostMethodSpec<THandlers>> methods);
    }

    internal sealed class SqliteHostDefinitionBuilder<THandlers> : ISqliteHostDefinitionBuilder<THandlers>
    {
        private readonly SqliteHostNamingBuilder _naming = new SqliteHostNamingBuilder();
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

        public SqliteHostDefinition<THandlers> Methods(IReadOnlyList<IHostMethodSpec<THandlers>> methods)
        {
            return new SqliteHostDefinition<THandlers>(_apiLevel, _minSqliteVersionNumber, _naming.Build(), methods);
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
            "scriptVars"
        };

        private readonly List<IRuntimeHostMethodSpec<THandlers>> _runtimeSpecs;
        private readonly Dictionary<string, IRuntimeHostMethodSpec<THandlers>> _specsByMethod;
        private readonly List<IHostMethodSpec<THandlers>> _methods;

        internal SqliteHostDefinition(
            int apiLevel,
            int minSqliteVersionNumber,
            SqliteHostNaming naming,
            IReadOnlyList<IHostMethodSpec<THandlers>> methods)
        {
            if (naming == null)
            {
                throw new ArgumentNullException(nameof(naming));
            }
            if (methods == null)
            {
                throw new ArgumentNullException(nameof(methods));
            }
            ApiLevel = apiLevel;
            MinSqliteVersionNumber = minSqliteVersionNumber;
            Naming = naming;
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
                || string.IsNullOrEmpty(naming.VarsTable))
            {
                throw new ArgumentException(
                    "Workspace table names (QueueTable, InputsTable, VarsTable) must be non-empty.",
                    nameof(naming));
            }
            if (string.Equals(naming.QueueTable, naming.InputsTable, StringComparison.Ordinal)
                || string.Equals(naming.QueueTable, naming.VarsTable, StringComparison.Ordinal)
                || string.Equals(naming.InputsTable, naming.VarsTable, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Workspace table names (QueueTable, InputsTable, VarsTable) must be mutually distinct.",
                    nameof(naming));
            }
            var workspaceTables = new HashSet<string>(StringComparer.Ordinal)
            {
                naming.QueueTable,
                naming.InputsTable,
                naming.VarsTable
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

        public int ApiLevel { get; }

        /// <summary>
        /// Minimum accepted SQLite version in the SQLITE_VERSION_NUMBER
        /// encoding (major*1000000 + minor*1000 + patch), e.g. 3019003;
        /// defaults to 3019003 when the builder's MinSqliteVersion is not
        /// called. Enforced by the runtime's workspace version gate.
        /// </summary>
        public int MinSqliteVersionNumber { get; }

        public SqliteHostNaming Naming { get; }

        public IReadOnlyList<IHostMethodSpec<THandlers>> Methods
        {
            get { return _methods; }
        }

        /// <summary>Protocol v1 features: typedNamedBindings, splitResultTables, scriptInputs, scriptVars.</summary>
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
            return SchemaGenerator.GenerateStatements(Naming, models);
        }

        /// <summary>Full DDL script — byte-identical to the committed DDL snapshot fixture.</summary>
        public string GenerateSchemaScript()
        {
            var models = new List<SchemaMethodModel>();
            foreach (IRuntimeHostMethodSpec<THandlers> spec in _runtimeSpecs)
            {
                models.Add(spec.SchemaModel);
            }
            return SchemaGenerator.GenerateScript(Naming, models);
        }

        internal IRuntimeHostMethodSpec<THandlers> ResolveSpec(string methodName)
        {
            IRuntimeHostMethodSpec<THandlers> spec;
            return methodName != null && _specsByMethod.TryGetValue(methodName, out spec) ? spec : null;
        }
    }
}
