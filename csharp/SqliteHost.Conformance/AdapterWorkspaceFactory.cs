using System;

namespace SqliteHost.Conformance
{
    /// <summary>
    /// Adapter-agnostic in-memory workspace factory for conformance and
    /// integration tests: counts OpenWorkspace calls and can retain the
    /// underlying workspace past the run via a no-op-dispose wrapper so
    /// tests can inspect final table contents. Deliberately NOT marked
    /// ISqliteHostScalarFunctionCapableFactory — the capability is a static
    /// factory-level promise; use
    /// <see cref="ScalarFunctionCapableAdapterWorkspaceFactory"/> when the
    /// opened adapter registers scalar functions.
    /// </summary>
    public class AdapterWorkspaceFactory : ISqliteHostConnectionFactory, IDisposable
    {
        private readonly Func<ISqliteHostConnection> _open;
        private readonly bool _retainWorkspace;

        public AdapterWorkspaceFactory(Func<ISqliteHostConnection> open, bool retainWorkspace = false)
        {
            _open = open;
            _retainWorkspace = retainWorkspace;
        }

        public int OpenCount { get; private set; }

        /// <summary>The most recently opened adapter (disposed by the runtime unless retained).</summary>
        public ISqliteHostConnection LastWorkspace { get; private set; }

        public ISqliteHostConnection OpenWorkspace()
        {
            OpenCount++;
            ISqliteHostConnection adapter = _open();
            LastWorkspace = adapter;
            return _retainWorkspace ? NonDisposingConnection.Wrap(adapter) : adapter;
        }

        public void Dispose()
        {
            if (_retainWorkspace && LastWorkspace != null)
            {
                LastWorkspace.Dispose();
            }
        }
    }

    /// <summary>
    /// <see cref="AdapterWorkspaceFactory"/> carrying the static
    /// scalar-function capability marker (feature inlineFunctions). Only
    /// wrap adapters whose connections implement
    /// ISqliteHostScalarFunctionConnection.
    /// </summary>
    public sealed class ScalarFunctionCapableAdapterWorkspaceFactory
        : AdapterWorkspaceFactory, ISqliteHostScalarFunctionCapableFactory
    {
        public ScalarFunctionCapableAdapterWorkspaceFactory(
            Func<ISqliteHostConnection> open, bool retainWorkspace = false)
            : base(open, retainWorkspace)
        {
        }
    }
}
