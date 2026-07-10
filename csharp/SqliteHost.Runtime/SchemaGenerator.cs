using System.Collections.Generic;
using System.Text;

namespace SqliteHost
{
    /// <summary>
    /// Workspace DDL generation (docs/workspace-schema.md). Mirrors the
    /// canonical implementation in codegen/core/src/ddl.ts; the output of
    /// <see cref="GenerateScript"/> is byte-identical to the committed DDL
    /// snapshot. Everything stays inside the SQLite 3.19.3 feature set.
    /// </summary>
    internal static class SchemaGenerator
    {
        public const string QueueTableName = "pending_host_calls";
        public const string InputsTableName = "script_inputs";
        public const string VarsTableName = "script_vars";

        public static string SqlColumnType(HostScalarType scalarType)
        {
            switch (scalarType)
            {
                case HostScalarType.Int32:
                case HostScalarType.Int64:
                case HostScalarType.Boolean:
                    return "INTEGER";
                case HostScalarType.String:
                    return "TEXT";
                case HostScalarType.Float32:
                case HostScalarType.Float64:
                    return "REAL";
                default:
                    return "BLOB";
            }
        }

        public static List<string> GenerateStatements(
            SqliteHostNaming naming,
            IReadOnlyList<SchemaMethodModel> methods)
        {
            var statements = new List<string>();
            statements.Add(QueueTableDdl());
            statements.Add(InputsTableDdl());
            statements.Add(VarsTableDdl());
            foreach (SchemaMethodModel method in methods)
            {
                statements.Add(ParentTableDdl(
                    NamingDerivation.CallTable(naming, method.MethodName),
                    InputColumnLines(naming, method.InputFields),
                    false));
                foreach (SchemaListFieldModel listField in method.InputListFields)
                {
                    statements.Add(ChildTableDdl(
                        NamingDerivation.InputListTable(naming, method.MethodName, listField.SqlName),
                        InputColumnLines(naming, listField.ItemFields)));
                }
                statements.Add(ParentTableDdl(
                    NamingDerivation.ResultTable(naming, method.MethodName),
                    ResultColumnLines(naming, method.ResultFields),
                    true));
                foreach (SchemaListFieldModel listField in method.ResultListFields)
                {
                    statements.Add(ChildTableDdl(
                        NamingDerivation.ResultListTable(naming, method.MethodName, listField.SqlName),
                        ResultColumnLines(naming, listField.ItemFields)));
                }
                statements.Add(QueueTriggerDdl(naming, method.MethodName));
            }
            return statements;
        }

        public static string GenerateScript(
            SqliteHostNaming naming,
            IReadOnlyList<SchemaMethodModel> methods)
        {
            List<string> statements = GenerateStatements(naming, methods);
            var builder = new StringBuilder();
            for (int i = 0; i < statements.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("\n\n");
                }
                builder.Append(statements[i]);
            }
            builder.Append("\n");
            return builder.ToString();
        }

        private static List<string> InputColumnLines(
            SqliteHostNaming naming,
            IReadOnlyList<SchemaFieldModel> fields)
        {
            var lines = new List<string>();
            foreach (SchemaFieldModel field in fields)
            {
                lines.Add(ScalarColumnLine(NamingDerivation.InputColumn(naming, field.SqlName), field));
            }
            return lines;
        }

        private static List<string> ResultColumnLines(
            SqliteHostNaming naming,
            IReadOnlyList<SchemaFieldModel> fields)
        {
            var lines = new List<string>();
            foreach (SchemaFieldModel field in fields)
            {
                lines.Add(ScalarColumnLine(NamingDerivation.ResultColumn(naming, field.SqlName), field));
            }
            return lines;
        }

        private static string ScalarColumnLine(string column, SchemaFieldModel field)
        {
            string notNull = field.Optional ? "" : " NOT NULL";
            return "    " + column + " " + SqlColumnType(field.ScalarType) + notNull;
        }

        private static string QueueTableDdl()
        {
            return "CREATE TABLE " + QueueTableName + " (\n"
                + "    queue_id INTEGER PRIMARY KEY AUTOINCREMENT,\n"
                + "    call_id TEXT NOT NULL UNIQUE,\n"
                + "    method TEXT NOT NULL,\n"
                + "    status TEXT NOT NULL DEFAULT 'pending'\n"
                + ");";
        }

        private static string InputsTableDdl()
        {
            return "CREATE TABLE " + InputsTableName + " (\n"
                + "    name TEXT NOT NULL PRIMARY KEY,\n"
                + "    value_type TEXT NOT NULL,\n"
                + "    int_value INTEGER,\n"
                + "    real_value REAL,\n"
                + "    text_value TEXT,\n"
                + "    blob_value BLOB\n"
                + ");";
        }

        /// <summary>
        /// Script scratch variable space (feature scriptVars): the runtime
        /// creates it empty and never reads or writes it.
        /// </summary>
        private static string VarsTableDdl()
        {
            return "CREATE TABLE " + VarsTableName + " (\n"
                + "    name TEXT NOT NULL PRIMARY KEY,\n"
                + "    value_type TEXT NOT NULL,\n"
                + "    int_value INTEGER,\n"
                + "    real_value REAL,\n"
                + "    text_value TEXT,\n"
                + "    blob_value BLOB\n"
                + ");";
        }

        private static string ParentTableDdl(string tableName, List<string> scalarColumnLines, bool isResultTable)
        {
            var columnLines = new List<string>();
            columnLines.Add("    call_id TEXT NOT NULL PRIMARY KEY");
            if (isResultTable)
            {
                columnLines.Add("    status TEXT NOT NULL DEFAULT 'done'");
            }
            columnLines.AddRange(scalarColumnLines);
            return "CREATE TABLE " + tableName + " (\n"
                + string.Join(",\n", columnLines) + "\n"
                + ");";
        }

        private static string ChildTableDdl(string tableName, List<string> scalarColumnLines)
        {
            var columnLines = new List<string>();
            columnLines.Add("    call_id TEXT NOT NULL");
            columnLines.Add("    item_index INTEGER NOT NULL");
            columnLines.AddRange(scalarColumnLines);
            columnLines.Add("    PRIMARY KEY (call_id, item_index)");
            return "CREATE TABLE " + tableName + " (\n"
                + string.Join(",\n", columnLines) + "\n"
                + ");";
        }

        private static string QueueTriggerDdl(SqliteHostNaming naming, string methodName)
        {
            return "CREATE TRIGGER " + NamingDerivation.QueueTrigger(naming, methodName) + "\n"
                + "AFTER INSERT ON " + NamingDerivation.CallTable(naming, methodName) + "\n"
                + "BEGIN\n"
                + "    INSERT INTO " + QueueTableName + " (call_id, method)\n"
                + "    VALUES (NEW.call_id, '" + methodName + "');\n"
                + "END;";
        }
    }
}
