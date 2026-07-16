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
        private int _minSqliteVersionNumber = SqliteHostDefinitionCore.DefaultMinSqliteVersionNumber;

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
    /// The generic parameter types the public API only; validation, spec
    /// resolution, and schema generation live in the non-generic
    /// <see cref="SqliteHostDefinitionCore"/>.
    /// </summary>
    public sealed class SqliteHostDefinition<THandlers>
    {
        private readonly SqliteHostDefinitionCore _core;
        private readonly List<IHostMethodSpec<THandlers>> _methods;

        internal SqliteHostDefinition(
            int apiLevel,
            int minSqliteVersionNumber,
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            IReadOnlyList<IHostMethodSpec<THandlers>> methods)
        {
            _core = new SqliteHostDefinitionCore(
                apiLevel, minSqliteVersionNumber, naming, columns, methods);
            _methods = new List<IHostMethodSpec<THandlers>>(methods);
        }

        /// <summary>The erased core the runtime consumes directly.</summary>
        internal SqliteHostDefinitionCore Core
        {
            get { return _core; }
        }

        public int ApiLevel
        {
            get { return _core.ApiLevel; }
        }

        /// <summary>
        /// Minimum accepted SQLite version in the SQLITE_VERSION_NUMBER
        /// encoding (major*1000000 + minor*1000 + patch), e.g. 3019003;
        /// defaults to 3019003 when the builder's MinSqliteVersion is not
        /// called. Enforced by the runtime's workspace version gate.
        /// </summary>
        public int MinSqliteVersionNumber
        {
            get { return _core.MinSqliteVersionNumber; }
        }

        public SqliteHostNaming Naming
        {
            get { return _core.Naming; }
        }

        public SqliteHostColumns Columns
        {
            get { return _core.Columns; }
        }

        public IReadOnlyList<IHostMethodSpec<THandlers>> Methods
        {
            get { return _methods; }
        }

        /// <summary>Protocol v1 features: typedNamedBindings, splitResultTables, scriptInputs, scriptVars, scriptControl.</summary>
        public IReadOnlyList<string> SupportedFeatures
        {
            get { return _core.SupportedFeatures; }
        }

        public IReadOnlyList<string> GenerateSchemaStatements()
        {
            return _core.GenerateSchemaStatements();
        }

        /// <summary>Full DDL script — byte-identical to the committed DDL snapshot fixture.</summary>
        public string GenerateSchemaScript()
        {
            return _core.GenerateSchemaScript();
        }
    }
}
