namespace SqliteHost
{
    /// <summary>
    /// Contract between generated method descriptors and the runtime.
    /// Members beyond these are implementation details of the Runtime
    /// package.
    /// </summary>
    public interface IHostMethodSpec<THandlers>
    {
        string MethodName { get; }
        int ApiLevel { get; }
    }
}
