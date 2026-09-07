namespace SqliteHost
{
    /// <summary>
    /// The one place physical names are read. A definition carries a
    /// resolved name only where the manifest's name differs from what the
    /// naming rules derive; everywhere else the slot is null and
    /// <see cref="NamingDerivation"/> supplies it (docs/naming.md).
    ///
    /// For a manifest the TypeSpec frontend produced that is EVERY slot:
    /// the frontend resolves names by those same rules
    /// (codegen/core/src/naming.ts), so generated code emits none of these
    /// calls and a literal per method and per field would cost app-size
    /// bytes to restate what the runtime already computes. The derivation
    /// is therefore load-bearing for generated hosts, not a fallback for
    /// hand-written ones, and NamingDerivationManifestTests pins it against
    /// the frontend's own manifest.
    ///
    /// The resolved slots fill in where a manifest was hand-written or
    /// rewritten and the two disagree. There the runtime must use the
    /// stored name rather than re-deriving a second answer that can
    /// disagree with the schema the same host shipped.
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
