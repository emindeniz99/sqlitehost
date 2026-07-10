namespace SqliteHost
{
    /// <summary>A named parameter binding. Names are bare (no :/@/$ prefix).</summary>
    public sealed class SqliteHostBinding
    {
        public SqliteHostBinding(string name, SqliteHostBindingValue value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>Bare parameter name, without the :/@/$ prefix.</summary>
        public string Name { get; }

        public SqliteHostBindingValue Value { get; }
    }
}
