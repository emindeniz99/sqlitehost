namespace SqliteHost
{
    /// <summary>Read access to one row of a query result, by column index.</summary>
    public interface ISqliteHostRow
    {
        bool IsNull(int index);
        int GetInt32(int index);
        long GetInt64(int index);
        bool GetBool(int index);
        string GetText(int index);
        byte[] GetBlob(int index);
        float GetFloat32(int index);
        double GetFloat64(int index);
    }
}
