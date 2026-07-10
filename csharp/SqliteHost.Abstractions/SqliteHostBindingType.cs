namespace SqliteHost
{
    /// <summary>Discriminator for <see cref="SqliteHostBindingValue"/> (protocol v1 binding types).</summary>
    public enum SqliteHostBindingType
    {
        Null,
        Int32,
        Int64,
        Bool,
        Text,
        Blob,
        Float32,
        Float64
    }
}
