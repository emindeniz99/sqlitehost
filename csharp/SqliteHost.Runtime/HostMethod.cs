namespace SqliteHost
{
    /// <summary>Entry point of the fluent method descriptor API (docs/csharp-api.md).</summary>
    public static class HostMethod
    {
        public static IHostMethodSpecBuilder<THandlers, TInput, TResult>
            For<THandlers, TInput, TResult>(string methodName)
            where TInput : new()
            where TResult : class
        {
            return new HostMethodSpecBuilder<THandlers, TInput, TResult>(methodName);
        }
    }
}
