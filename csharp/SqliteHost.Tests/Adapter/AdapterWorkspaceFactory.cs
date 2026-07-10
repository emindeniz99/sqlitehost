using System;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Adapter-agnostic in-memory workspace factory for the multi-adapter
    /// integration tests: counts OpenWorkspace calls and can retain the
    /// underlying workspace past the run via a no-op-dispose wrapper so
    /// tests can inspect final table contents (same contract as
    /// TestWorkspaceFactory, generalized over any adapter opener).
    /// </summary>
    public sealed class AdapterWorkspaceFactory : ISqliteHostConnectionFactory, IDisposable
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
            return _retainWorkspace ? new NonDisposingConnection(adapter) : adapter;
        }

        public void Dispose()
        {
            if (_retainWorkspace && LastWorkspace != null)
            {
                LastWorkspace.Dispose();
            }
        }
    }
}
