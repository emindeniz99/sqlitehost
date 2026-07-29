using System;
using System.IO;

namespace SqliteHost.Tests.Fixtures
{
    public static class FixturePaths
    {
        /// <summary>Absolute path of projects/sqlitehost/fixtures, found by walking up from the test binaries.</summary>
        public static string Root { get; } = FindRoot();

        public static string Schema(string fileName)
            => Path.Combine(Root, "schemas", fileName);

        public static string Payload(string relativePath)
            => Path.Combine(Root, "payloads", relativePath);

        public static string Delivery(string relativePath)
            => Path.Combine(Root, "delivery", relativePath);

        private static string FindRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string marker = Path.Combine(directory.FullName, "fixtures", "manifests", "sample-host.manifest.json");
                if (File.Exists(marker))
                {
                    return Path.Combine(directory.FullName, "fixtures");
                }
                directory = directory.Parent;
            }
            throw new InvalidOperationException(
                "Could not locate the fixtures directory above " + AppContext.BaseDirectory);
        }
    }
}
