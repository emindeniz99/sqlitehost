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
        ISqliteHostDefinitionBuilder<THandlers> Naming(Action<SqliteHostNamingBuilder> configure);
        SqliteHostDefinition<THandlers> Methods(IReadOnlyList<IHostMethodSpec<THandlers>> methods);
    }

    internal sealed class SqliteHostDefinitionBuilder<THandlers> : ISqliteHostDefinitionBuilder<THandlers>
    {
        private readonly SqliteHostNamingBuilder _naming = new SqliteHostNamingBuilder();
        private int _apiLevel = 1;

        public ISqliteHostDefinitionBuilder<THandlers> ApiLevel(int apiLevel)
        {
            _apiLevel = apiLevel;
            return this;
        }

        public ISqliteHostDefinitionBuilder<THandlers> Naming(Action<SqliteHostNamingBuilder> configure)
        {
            configure(_naming);
            return this;
        }

        public SqliteHostDefinition<THandlers> Methods(IReadOnlyList<IHostMethodSpec<THandlers>> methods)
        {
            return new SqliteHostDefinition<THandlers>(_apiLevel, _naming.Build(), methods);
        }
    }

    /// <summary>
    /// The host definition: API level, naming conventions, registered
    /// method specs, supported features, and workspace schema generation.
    /// </summary>
    public sealed class SqliteHostDefinition<THandlers>
    {
        private static readonly IReadOnlyList<string> FeaturesV1 = new List<string>
        {
            "typedNamedBindings",
            "splitResultTables",
            "scriptInputs"
        };

        private readonly List<IRuntimeHostMethodSpec<THandlers>> _runtimeSpecs;
        private readonly Dictionary<string, IRuntimeHostMethodSpec<THandlers>> _specsByMethod;
        private readonly List<IHostMethodSpec<THandlers>> _methods;

        internal SqliteHostDefinition(
            int apiLevel,
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
        }

        public int ApiLevel { get; }

        public SqliteHostNaming Naming { get; }

        public IReadOnlyList<IHostMethodSpec<THandlers>> Methods
        {
            get { return _methods; }
        }

        /// <summary>Protocol v1 features: typedNamedBindings, splitResultTables, scriptInputs.</summary>
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
