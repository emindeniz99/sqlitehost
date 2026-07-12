using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SqliteHost.Tests.Fixtures;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Source-level guards over SqliteHost.Runtime, SqliteHost.Abstractions,
    /// and SqliteHost.Adapters.Native (docs/errors.md logging policy,
    /// docs/compatibility.md IL2CPP guardrail): the shipped packages emit no
    /// logs and use no reflection. The tests scan the actual .cs sources at
    /// test time, ignoring comment text. No exceptions are allowed today; if
    /// one ever becomes necessary it must be added here explicitly with a
    /// justification.
    /// </summary>
    public class SourceGuardTests
    {
        private static readonly string[] LoggingTokens =
        {
            "Console.",
            "Debug.Log",
            "Trace.Write"
        };

        private static readonly string[] ReflectionTokens =
        {
            "System.Reflection",
            "GetType().GetMethod",
            "Activator.CreateInstance",
            "Reflection.Emit"
        };

        [Fact]
        public void RuntimeAndAbstractionsSources_EmitNoLogs()
        {
            AssertNoForbiddenTokens(LoggingTokens);
        }

        [Fact]
        public void RuntimeAndAbstractionsSources_UseNoReflection()
        {
            AssertNoForbiddenTokens(ReflectionTokens);
        }

        private static void AssertNoForbiddenTokens(string[] tokens)
        {
            var violations = new List<string>();
            foreach (string file in GuardedSourceFiles())
            {
                string code = StripComments(File.ReadAllText(file));
                foreach (string token in tokens)
                {
                    if (code.Contains(token))
                    {
                        violations.Add(Path.GetFileName(Path.GetDirectoryName(file))
                            + "/" + Path.GetFileName(file) + " contains '" + token + "'");
                    }
                }
            }
            Assert.True(violations.Count == 0,
                "Forbidden tokens in shipped sources:\n" + string.Join("\n", violations));
        }

        /// <summary>Top-level .cs files of the shipped runtime-side packages (bin/obj excluded by construction).</summary>
        private static IEnumerable<string> GuardedSourceFiles()
        {
            string csharpRoot = Path.Combine(Directory.GetParent(FixturePaths.Root).FullName, "csharp");
            string[] projects = { "SqliteHost.Runtime", "SqliteHost.Abstractions", "SqliteHost.Adapters.Native" };
            bool any = false;
            foreach (string project in projects)
            {
                foreach (string file in Directory.GetFiles(
                    Path.Combine(csharpRoot, project), "*.cs", SearchOption.TopDirectoryOnly))
                {
                    any = true;
                    yield return file;
                }
            }
            if (!any)
            {
                throw new InvalidOperationException("Guard found no sources to scan under " + csharpRoot);
            }
        }

        /// <summary>
        /// Removes // line comments and /* */ block comments while keeping
        /// string/char literal contents intact, so forbidden tokens inside
        /// comments do not count and tokens inside code always do.
        /// </summary>
        private static string StripComments(string source)
        {
            var result = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                }
                else if (c == '/' && next == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i += 2;
                }
                else if (c == '"')
                {
                    // string literal (handles \" escapes; shipped sources use no verbatim strings)
                    result.Append(c);
                    i++;
                    while (i < source.Length && source[i] != '"')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            result.Append(source[i]);
                            i++;
                        }
                        result.Append(source[i]);
                        i++;
                    }
                    if (i < source.Length)
                    {
                        result.Append(source[i]);
                        i++;
                    }
                }
                else
                {
                    result.Append(c);
                    i++;
                }
            }
            return result.ToString();
        }
    }
}
