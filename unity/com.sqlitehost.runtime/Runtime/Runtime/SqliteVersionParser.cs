namespace SqliteHost
{
    /// <summary>
    /// Parses sqlite_version() strings into the SQLITE_VERSION_NUMBER
    /// encoding (major*1000000 + minor*1000 + patch), tolerating the
    /// historical 4-component form ("3.8.11.1" → 3008011, 4th component
    /// ignored) per docs/errors.md.
    /// </summary>
    internal static class SqliteVersionParser
    {
        /// <summary>
        /// Accepts 1-4 dot-separated non-negative integer components;
        /// missing minor/patch count as 0. Returns false for anything else.
        /// </summary>
        public static bool TryParse(string versionString, out int versionNumber)
        {
            versionNumber = 0;
            if (string.IsNullOrEmpty(versionString))
            {
                return false;
            }
            string[] components = versionString.Trim().Split('.');
            if (components.Length < 1 || components.Length > 4)
            {
                return false;
            }
            int major = 0;
            int minor = 0;
            int patch = 0;
            for (int i = 0; i < components.Length && i < 3; i++)
            {
                int component;
                if (!TryParseComponent(components[i], out component))
                {
                    return false;
                }
                if (i == 0)
                {
                    major = component;
                }
                else if (i == 1)
                {
                    minor = component;
                }
                else
                {
                    patch = component;
                }
            }
            // A 4th component is ignored for the number, but it must still
            // be numeric for the string to count as a version at all.
            if (components.Length == 4)
            {
                int ignored;
                if (!TryParseComponent(components[3], out ignored))
                {
                    return false;
                }
            }
            versionNumber = major * 1000000 + minor * 1000 + patch;
            return true;
        }

        private static bool TryParseComponent(string component, out int value)
        {
            value = 0;
            // The number encoding gives each component 3 decimal digits, so
            // anything longer cannot be a real version component (and this
            // keeps the arithmetic overflow-free).
            if (string.IsNullOrEmpty(component) || component.Length > 3)
            {
                return false;
            }
            int parsed = 0;
            for (int i = 0; i < component.Length; i++)
            {
                char c = component[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }
                parsed = parsed * 10 + (c - '0');
            }
            value = parsed;
            return true;
        }
    }
}
