namespace SqliteHost
{
    /// <summary>One successfully executed host call, for diagnostics.</summary>
    public sealed class SqliteHostCallDiagnostic
    {
        public string CallId { get; set; }
        public string Method { get; set; }
        public string StepId { get; set; }
    }
}
