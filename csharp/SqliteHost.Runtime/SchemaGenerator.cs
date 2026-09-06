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
            SqliteHostColumns columns,
            IReadOnlyList<SchemaMethodModel> methods)
        {
            var statements = new List<string>();
            statements.Add(QueueTableDdl(naming, columns));
            statements.Add(NameValueTableDdl(naming.InputsTable, columns));
            statements.Add(NameValueTableDdl(naming.VarsTable, columns));
            statements.Add(ControlTableDdl(naming, columns));
            foreach (SchemaMethodModel method in methods)
            {
                statements.Add(ParentTableDdl(
                    ResolvedNames.CallTable(naming, method),
                    columns,
                    InputColumnLines(naming, method.InputFields),
                    false));
                foreach (SchemaListFieldModel listField in method.InputListFields)
                {
                    statements.Add(ChildTableDdl(
                        ResolvedNames.InputListTable(naming, method, listField),
                        columns,
                        InputColumnLines(naming, listField.ItemFields)));
                }
                statements.Add(ParentTableDdl(
                    ResolvedNames.ResultTable(naming, method),
                    columns,
                    ResultColumnLines(naming, method.ResultFields),
                    true));
                foreach (SchemaListFieldModel listField in method.ResultListFields)
                {
                    statements.Add(ChildTableDdl(
                        ResolvedNames.ResultListTable(naming, method, listField),
                        columns,
                        ResultColumnLines(naming, listField.ItemFields)));
                }
                statements.Add(QueueTriggerDdl(naming, columns, method));
            }
            return statements;
        }

        public static string GenerateScript(
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            IReadOnlyList<SchemaMethodModel> methods)
        {
            List<string> statements = GenerateStatements(naming, columns, methods);
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
                lines.Add(ScalarColumnLine(ResolvedNames.InputColumn(naming, field), field));
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
                lines.Add(ScalarColumnLine(ResolvedNames.ResultColumn(naming, field), field));
            }
            return lines;
        }

        private static string ScalarColumnLine(string column, SchemaFieldModel field)
        {
            string notNull = field.Optional ? "" : " NOT NULL";
            return "    " + column + " " + SqlColumnType(field.ScalarType) + notNull;
        }

        private static string QueueTableDdl(SqliteHostNaming naming, SqliteHostColumns columns)
        {
            return "CREATE TABLE " + naming.QueueTable + " (\n"
                + "    " + columns.QueueId + " INTEGER PRIMARY KEY AUTOINCREMENT,\n"
                + "    " + columns.CallId + " TEXT NOT NULL UNIQUE,\n"
                + "    " + columns.Method + " TEXT NOT NULL,\n"
                + "    " + columns.Status + " TEXT NOT NULL DEFAULT '" + ProtocolConstants.PendingStatus + "'\n"
                + ");";
        }

        /// <summary>
        /// Shared shape of the inputs table and the script scratch variable
        /// table (feature scriptVars); the runtime creates the vars table
        /// empty and never reads or writes it.
        /// </summary>
        private static string NameValueTableDdl(string tableName, SqliteHostColumns columns)
        {
            return "CREATE TABLE " + tableName + " (\n"
                + "    " + columns.Name + " TEXT NOT NULL PRIMARY KEY,\n"
                + "    " + columns.ValueType + " TEXT NOT NULL,\n"
                + "    " + columns.IntValue + " INTEGER,\n"
                + "    " + columns.RealValue + " REAL,\n"
                + "    " + columns.TextValue + " TEXT,\n"
                + "    " + columns.BlobValue + " BLOB\n"
                + ");";
        }

        /// <summary>
        /// Script early-exit channel (feature scriptControl): the runtime
        /// creates it empty, checks it after every statement, and never
        /// writes it.
        /// </summary>
        private static string ControlTableDdl(SqliteHostNaming naming, SqliteHostColumns columns)
        {
            return "CREATE TABLE " + naming.ControlTable + " (\n"
                + "    " + columns.Action + " TEXT NOT NULL,\n"
                + "    " + columns.Message + " TEXT\n"
                + ");";
        }

        private static string ParentTableDdl(
            string tableName,
            SqliteHostColumns columns,
            List<string> scalarColumnLines,
            bool isResultTable)
        {
            var columnLines = new List<string>();
            columnLines.Add("    " + columns.CallId + " TEXT NOT NULL PRIMARY KEY");
            if (isResultTable)
            {
                // DoneValue is data, not an identifier: escape embedded
                // quotes so a value like "do'ne" cannot break the literal.
                columnLines.Add("    " + columns.Status + " TEXT NOT NULL DEFAULT '"
                    + columns.DoneValue.Replace("'", "''") + "'");
            }
            columnLines.AddRange(scalarColumnLines);
            return "CREATE TABLE " + tableName + " (\n"
                + string.Join(",\n", columnLines) + "\n"
                + ");";
        }

        private static string ChildTableDdl(
            string tableName,
            SqliteHostColumns columns,
            List<string> scalarColumnLines)
        {
            var columnLines = new List<string>();
            columnLines.Add("    " + columns.CallId + " TEXT NOT NULL");
            columnLines.Add("    " + columns.ItemIndex + " INTEGER NOT NULL");
            columnLines.AddRange(scalarColumnLines);
            columnLines.Add("    PRIMARY KEY (" + columns.CallId + ", " + columns.ItemIndex + ")");
            return "CREATE TABLE " + tableName + " (\n"
                + string.Join(",\n", columnLines) + "\n"
                + ");";
        }

        private static string QueueTriggerDdl(
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            SchemaMethodModel method)
        {
            string methodName = method.MethodName;
            return "CREATE TRIGGER " + ResolvedNames.QueueTrigger(naming, method) + "\n"
                + "AFTER INSERT ON " + ResolvedNames.CallTable(naming, method) + "\n"
                + "BEGIN\n"
                + "    INSERT INTO " + naming.QueueTable + " (" + columns.CallId + ", " + columns.Method + ")\n"
                + "    VALUES (NEW." + columns.CallId + ", '" + methodName + "');\n"
                + "END;";
        }
    }
}
