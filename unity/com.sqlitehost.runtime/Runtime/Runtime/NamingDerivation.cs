using System.Text;

namespace SqliteHost
{
    /// <summary>
    /// Physical name derivation (docs/naming.md). Mirrors the canonical
    /// implementation in codegen/core/src/naming.ts.
    /// </summary>
    internal static class NamingDerivation
    {
        /// <summary>
        /// Insert "_" before an uppercase letter that follows a lowercase
        /// letter or digit, or that is followed by a lowercase letter;
        /// lowercase everything.
        /// </summary>
        public static string ToSnakeCase(string name)
        {
            var builder = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (ch >= 'A' && ch <= 'Z')
                {
                    char prev = i > 0 ? name[i - 1] : '\0';
                    char next = i + 1 < name.Length ? name[i + 1] : '\0';
                    bool prevIsLowerOrDigit =
                        (prev >= 'a' && prev <= 'z') || (prev >= '0' && prev <= '9');
                    bool nextIsLower = next >= 'a' && next <= 'z';
                    if (i > 0 && (prevIsLowerOrDigit || nextIsLower))
                    {
                        builder.Append('_');
                    }
                    builder.Append((char)(ch + ('a' - 'A')));
                }
                else
                {
                    builder.Append(ch);
                }
            }
            return builder.ToString();
        }

        public static string CallTable(SqliteHostNaming naming, string methodName)
        {
            return naming.CallTablePrefix + ToSnakeCase(methodName);
        }

        public static string ResultTable(SqliteHostNaming naming, string methodName)
        {
            return naming.ResultTablePrefix + ToSnakeCase(methodName);
        }

        public static string InputColumn(SqliteHostNaming naming, string sqlName)
        {
            return naming.InputColumnPrefix + sqlName;
        }

        public static string ResultColumn(SqliteHostNaming naming, string sqlName)
        {
            return naming.ResultColumnPrefix + sqlName;
        }

        public static string InputListTable(SqliteHostNaming naming, string methodName, string sqlName)
        {
            return CallTable(naming, methodName) + naming.InputListTableInfix + sqlName;
        }

        public static string ResultListTable(SqliteHostNaming naming, string methodName, string sqlName)
        {
            return ResultTable(naming, methodName) + naming.ResultListTableInfix + sqlName;
        }

        public static string QueueTrigger(SqliteHostNaming naming, string methodName)
        {
            return "trg_" + CallTable(naming, methodName) + "_queue";
        }
    }
}
