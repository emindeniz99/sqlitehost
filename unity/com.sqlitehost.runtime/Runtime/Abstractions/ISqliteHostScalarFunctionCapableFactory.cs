namespace SqliteHost
{
    /// <summary>
    /// Marker on the factory = static capability: the runtime knows whether
    /// inlineFunctions is supported WITHOUT opening a workspace, so the
    /// clean-skip precheck stays workspace-free. Connections returned by a
    /// capable factory must implement
    /// <see cref="ISqliteHostScalarFunctionConnection"/>.
    /// </summary>
    public interface ISqliteHostScalarFunctionCapableFactory : ISqliteHostConnectionFactory
    {
    }
}
