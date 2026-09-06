namespace SqliteHost
{
    /// <summary>
    /// SQLite's five storage classes, as reported by
    /// <c>sqlite3_column_type</c> for the value actually stored in a column
    /// — not the column's declared type or affinity. A column declared
    /// INTEGER holds a TEXT value whenever the conversion would have lost
    /// information, so the two answers differ routinely
    /// (docs/adapter-contract.md, "Value fidelity").
    /// </summary>
    public enum SqliteHostStorageClass
    {
        Null,
        Integer,
        Real,
        Text,
        Blob
    }
}
