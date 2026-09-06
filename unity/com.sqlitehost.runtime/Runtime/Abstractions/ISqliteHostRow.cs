namespace SqliteHost
{
    /// <summary>
    /// Read access to one row of a query result, by column index.
    ///
    /// <para><b>NULL is not a value.</b> Every typed getter below requires
    /// a non-NULL column: on a NULL it throws
    /// <see cref="System.InvalidOperationException"/> naming the column,
    /// and never substitutes 0, an empty string, an empty array or a null
    /// reference. The two silent alternatives are both losses — a
    /// substituted zero is indistinguishable from a stored zero, and a null
    /// reference just moves the failure to the caller's next dereference —
    /// so <see cref="IsNull"/> is the only way to ask, and asking it first
    /// is the caller's job. The conformance suite pins this for every
    /// adapter (docs/adapter-contract.md, "Value fidelity").</para>
    /// </summary>
    public interface ISqliteHostRow
    {
        /// <summary>True when the column holds SQL NULL. Ask before any getter below.</summary>
        bool IsNull(int index);

        /// <summary>The column as int32. Throws on a NULL column.</summary>
        int GetInt32(int index);

        /// <summary>The column as int64. Throws on a NULL column.</summary>
        long GetInt64(int index);

        /// <summary>The column as a bool (0 is false, anything else true). Throws on a NULL column.</summary>
        bool GetBool(int index);

        /// <summary>
        /// The column as text; never null. An empty string is a stored
        /// empty string, not a missing value. Throws on a NULL column.
        /// </summary>
        string GetText(int index);

        /// <summary>
        /// The column as bytes; never null. A zero-length array is a stored
        /// EMPTY blob, which is a different value from NULL and stays
        /// distinguishable from it: NULL throws. Throws on a NULL column.
        /// </summary>
        byte[] GetBlob(int index);

        /// <summary>
        /// The column as float32. Throws on a NULL column. ±Infinity and
        /// NaN are legitimate REAL values here and are returned as read —
        /// the finite-only rule belongs to the JSON envelope, not to a
        /// value the engine handed back.
        /// </summary>
        float GetFloat32(int index);

        /// <summary>The column as float64. Throws on a NULL column; see <see cref="GetFloat32"/> on non-finite values.</summary>
        double GetFloat64(int index);
    }
}
