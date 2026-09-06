namespace SqliteHost
{
    /// <summary>
    /// The one place physical names are read. A definition built from a
    /// generated host carries every physical name the manifest already
    /// resolved (codegen/core/src/naming.ts: the frontend resolves names,
    /// emitters only read them), so the runtime must use those rather than
    /// re-deriving a second answer that can disagree with the schema the
    /// same host shipped.
    ///
    /// A hand-written definition declares logical names only; there the
    /// resolved slots are null and <see cref="NamingDerivation"/> supplies
    /// the name, which is what keeps the derivation the documented default
    /// (docs/naming.md) instead of a second source of truth.
    /// </summary>
    internal static class ResolvedNames
    {
        public static string CallTable(SqliteHostNaming naming, SchemaMethodModel method)
        {
            return method.CallTable ?? NamingDerivation.CallTable(naming, method.MethodName);
        }

        public static string ResultTable(SqliteHostNaming naming, SchemaMethodModel method)
        {
            return method.ResultTable ?? NamingDerivation.ResultTable(naming, method.MethodName);
        }

        public static string QueueTrigger(SqliteHostNaming naming, SchemaMethodModel method)
        {
            return method.QueueTrigger ?? NamingDerivation.QueueTrigger(naming, method.MethodName);
        }

        public static string InputListTable(
            SqliteHostNaming naming,
            SchemaMethodModel method,
            SchemaListFieldModel listField)
        {
            return listField.ChildTable
                ?? NamingDerivation.InputListTable(naming, method.MethodName, listField.SqlName);
        }

        public static string ResultListTable(
            SqliteHostNaming naming,
            SchemaMethodModel method,
            SchemaListFieldModel listField)
        {
            return listField.ChildTable
                ?? NamingDerivation.ResultListTable(naming, method.MethodName, listField.SqlName);
        }

        public static string InputColumn(SqliteHostNaming naming, SchemaFieldModel field)
        {
            return field.Column ?? NamingDerivation.InputColumn(naming, field.SqlName);
        }

        public static string ResultColumn(SqliteHostNaming naming, SchemaFieldModel field)
        {
            return field.Column ?? NamingDerivation.ResultColumn(naming, field.SqlName);
        }
    }
}
