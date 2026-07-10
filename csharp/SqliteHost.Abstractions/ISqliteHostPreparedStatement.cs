using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>A prepared (compiled, never stepped) statement exposing parameter metadata.</summary>
    public interface ISqliteHostPreparedStatement : IDisposable
    {
        IReadOnlyList<string> ParameterNames { get; }
    }
}
