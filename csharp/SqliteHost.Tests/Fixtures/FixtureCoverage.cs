using System;
using System.Collections.Generic;
using System.IO;

namespace SqliteHost.Tests.Fixtures
{
    /// <summary>
    /// Which committed payload fixtures the C# runtime executes, and — for
    /// the ones it does not — why.
    ///
    /// <para>The corpus was opt-IN before this file existed: a test named
    /// each payload it ran, so a fixture added by a contract change was
    /// validated by Java and TypeScript and silently never executed by the
    /// runtime that has to run it. Six valid payloads had accumulated that
    /// way. Coverage is opt-OUT now: the suites below enumerate
    /// <c>fixtures/payloads/{valid,invalid}/</c> and every file must either
    /// run or appear in a skip table WITH A REASON.</para>
    ///
    /// <para><c>scripts/check-fixture-corpus.mjs</c> parses this file as the
    /// evidence for its third consumer id, <c>csharp</c>: directory listing
    /// minus skip tables. That is why the tables are delimited by the
    /// <c>&gt;&gt;&gt;</c>/<c>&lt;&lt;&lt;</c> marker comments and why every
    /// entry is one <c>{ "file.json", "reason" }</c> pair — a new fixture
    /// nobody wired up fails that checker, in CI, with the file name.</para>
    /// </summary>
    internal static class FixtureCoverage
    {
        /// <summary>
        /// <c>valid/</c> payloads <see cref="IntegrationFixtureTestsBase"/>
        /// does not run end to end, with the reason. EMPTY, and worth
        /// keeping that way: every valid payload is a script the runtime is
        /// supposed to execute, so a reason here is an admission that the
        /// test host cannot build the definition it binds to. Prefer
        /// teaching the test host the manifest.
        /// </summary>
        // >>> valid-skip-list (parsed by scripts/check-fixture-corpus.mjs)
        internal static readonly IReadOnlyDictionary<string, string> UnrunValid =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
            };
        // <<< valid-skip-list

        /// <summary>
        /// Valid payloads whose SQL needs an engine newer than the sample
        /// host's floor, mapped to the sqlite3_libversion_number they need.
        ///
        /// <para>Found by running the corpus on a real 3.19.3 build once the
        /// directory-driven suite existed: <c>example-011-insert-alias</c>
        /// spells <c>INSERT INTO t AS alias</c>, which arrived with UPSERT in
        /// 3.24.0 and is a syntax error on 3.19.3 — the floor
        /// docs/compatibility.md documents and the engine matrix runs. Both
        /// validators accept the payload, because their layer-3 prepare uses
        /// whatever engine the validator happens to link; neither has a
        /// version rule for SYNTAX (the
        /// <c>sqlite-version-too-low-for-function</c> lint covers functions
        /// only). So this table is the record of a real corpus gap, not a
        /// test convenience: it is the reason the fixture skips on the two
        /// below-3.24 matrix legs instead of failing them.</para>
        /// </summary>
        // >>> valid-engine-floors (parsed by scripts/check-fixture-corpus.mjs)
        internal static readonly IReadOnlyDictionary<string, int> ValidEngineFloors =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "example-011-insert-alias.json", 3024000 },
            };
        // <<< valid-engine-floors

        /// <summary>
        /// <c>invalid/</c> payloads the runtime refuses at the envelope
        /// layer, mapped to the <c>ErrorCode</c> it reports. Every one of
        /// these is refused BEFORE a workspace opens, which is the property
        /// <see cref="InvalidFixtureEnvelopeTests"/> asserts alongside the
        /// code — the envelope precheck is the runtime's own reading of
        /// docs/script-envelope.md and it must not need an engine.
        /// </summary>
        // >>> invalid-envelope-refusals (parsed by scripts/check-fixture-corpus.mjs)
        internal static readonly IReadOnlyDictionary<string, string> EnvelopeRefusals =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "api-level-too-high.json", "unsupported-api-level" },
                { "blank-required-feature.json", "missing-feature" },
                { "blank-statement-sql.json", "invalid-script" },
                { "blank-step-id.json", "invalid-script" },
                { "duplicate-input-name.json", "duplicate-input-name" },
                { "duplicate-step-id.json", "duplicate-step-id" },
                { "empty-statements.json", "invalid-script" },
                { "method-api-level-too-high.json", "missing-method" },
                { "unknown-engine.json", "unsupported-engine" },
                { "unknown-required-feature.json", "missing-feature" },
                { "unknown-required-method.json", "missing-method" },
            };
        // <<< invalid-envelope-refusals

        /// <summary>
        /// <c>invalid/</c> payloads the C# JSON reader refuses before the
        /// runtime sees them, mapped to a fragment of the exception message.
        /// The reader is test-only (the runtime consumes parsed objects),
        /// but it is the C# side of the same envelope contract the Java and
        /// TypeScript readers implement, so a payload they refuse and it
        /// accepts is a real divergence.
        /// </summary>
        // >>> invalid-reader-refusals (parsed by scripts/check-fixture-corpus.mjs)
        internal static readonly IReadOnlyDictionary<string, string> ReaderRefusals =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "non-integral-int.json", "invalid format" },
            };
        // <<< invalid-reader-refusals

        /// <summary>
        /// Every remaining <c>invalid/</c> payload, with the reason the
        /// runtime does not refuse it at the envelope layer. Two classes,
        /// and the reason string says which:
        ///
        /// <para><b>lint-only</b> — the code is an authoring rule. The
        /// runtime is not a validator (docs/validation.md): it hands the
        /// statement to SQLite, which either executes it or reports its own
        /// error. Pinning an outcome here would pin whichever engine and
        /// build flags the cell happens to run, and the engine matrix runs
        /// five of them.</para>
        ///
        /// <para><b>post-envelope</b> — the runtime DOES refuse it, but only
        /// after the workspace is open (bind time, drain time, or SQLite
        /// itself). Those refusals are pinned by the suites that own each
        /// mechanism; repeating them here would duplicate coverage and make
        /// this table engine-dependent.</para>
        ///
        /// <para><b>gap</b> — the case is envelope-class in
        /// expectations.json and the C# side does NOT refuse it. Three of
        /// them, all real. They are listed rather than hidden.</para>
        /// </summary>
        // >>> invalid-not-envelope (parsed by scripts/check-fixture-corpus.mjs)
        internal static readonly IReadOnlyDictionary<string, string> NotEnvelopeFaults =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "binding-type-mismatch-call-id.json", "lint-only: binding-type-mismatch is an authoring rule; SQLite is untyped and stores what it is given" },
                { "binding-type-mismatch-float.json", "post-envelope: refused at drain as input-type-mismatch (RuntimeStorageClassTests owns that)" },
                { "binding-type-mismatch.json", "post-envelope: refused at drain as input-type-mismatch (RuntimeStorageClassTests owns that)" },
                { "blank-binding-name.json", "gap: invalid-envelope in expectations.json, but the precheck does not walk binding names; the blank one surfaces later as unused-binding" },
                { "duplicate-call-id-backtick.json", "post-envelope: the protocol table's primary key rejects it as a sql-error" },
                { "duplicate-call-id-cross-method.json", "post-envelope: the protocol table's primary key rejects it as a sql-error" },
                { "duplicate-call-id.json", "post-envelope: the protocol table's primary key rejects it as a sql-error" },
                { "embedded-nul.json", "lint-only: the runtime hands the text to SQLite, whose own truncation-at-NUL behaviour is the engine's" },
                { "forbidden-function-pragma-optimize.json", "lint-only: forbidden-function is an authoring rule; the runtime executes the call" },
                { "forbidden-statement-attach.json", "lint-only: forbidden-statement is an authoring rule; whether ATTACH succeeds is the engine's and the sandbox's" },
                { "forbidden-statement-begin.json", "lint-only: forbidden-statement is an authoring rule; the runtime executes the statement" },
                { "forbidden-statement-bom-pragma.json", "lint-only: forbidden-statement is an authoring rule; the runtime executes the statement" },
                { "forbidden-statement-drop.json", "lint-only: forbidden-statement is an authoring rule; the runtime executes the statement" },
                { "forbidden-statement-explain.json", "lint-only: forbidden-statement is an authoring rule; the runtime executes the statement" },
                { "forbidden-statement-pragma.json", "lint-only: forbidden-statement is an authoring rule; the runtime executes the statement" },
                { "implicit-column-list.json", "lint-only: implicit-column-list is an authoring rule; the INSERT is valid SQL" },
                { "inline-arity-mismatch.json", "post-envelope: the unregistered function makes SQLite refuse the statement" },
                { "inline-undeclared-feature.json", "lint-only: undeclared-feature-use is an authoring rule; the runtime only checks features it was ASKED for" },
                { "inline-unknown-function.json", "post-envelope: the unregistered function makes SQLite refuse the statement" },
                { "list-child-later-step.json", "post-envelope: refused at bind as list-child-after-drain, and that check is stripped under SQLITEHOST_SLIM (RuntimeDrainTests owns it)" },
                { "list-child-without-parent.json", "lint-only: list-child-without-parent is an authoring rule; the orphan row is ordinary SQL" },
                { "missing-binding.json", "post-envelope: refused at bind as missing-binding (the conformance suite owns that)" },
                { "multiple-statements.json", "lint-only: multiple-statements is an authoring rule; the adapter contract already forbids the second statement running" },
                { "non-canonical-base64.json", "gap: invalid-envelope in expectations.json, but Convert.FromBase64String accepts non-zero discarded bits, so the reader takes it" },
                { "nonportable-function.json", "lint-only: nonportable-function is an authoring rule; whether the build has the function is the engine's" },
                { "nonportable-load-extension.json", "lint-only: nonportable-function is an authoring rule; whether load_extension exists is a compile-time flag of the engine" },
                { "nonportable-soundex.json", "lint-only: nonportable-function is an authoring rule; whether the build has soundex is a compile-time flag of the engine" },
                { "null-optional-field.json", "gap: invalid-envelope in expectations.json, but the reader treats a JSON null for an optional string as absent" },
                { "positional-parameter.json", "post-envelope: the unbound positional parameter makes the bind fail" },
                { "protocol-table-write-single-quoted-delete.json", "lint-only: protocol-table-write is an authoring rule; the statement is ordinary SQL to the engine" },
                { "protocol-table-write-single-quoted-insert.json", "lint-only: protocol-table-write is an authoring rule; the statement is ordinary SQL to the engine" },
                { "protocol-table-write-single-quoted-qualified.json", "lint-only: protocol-table-write is an authoring rule; the statement is ordinary SQL to the engine" },
                { "protocol-table-write-single-quoted-update.json", "lint-only: protocol-table-write is an authoring rule; the statement is ordinary SQL to the engine" },
                { "protocol-table-write-sqlite-master.json", "lint-only: protocol-table-write is an authoring rule; SQLite's own refusal to write sqlite_master is the engine's, not the runtime's" },
                { "protocol-table-write-vertical-tab.json", "lint-only: protocol-table-write is an authoring rule; the statement does not parse on this engine, which is the engine's answer" },
                { "protocol-table-write.json", "lint-only: protocol-table-write is an authoring rule; the statement is ordinary SQL to the engine" },
                { "result-read-not-after-call.json", "lint-only: result-read-not-after-call is an authoring rule; reading an empty result table is ordinary SQL" },
                { "result-read-single-quoted-unknown-call.json", "lint-only: result-read-unknown-call is an authoring rule; reading an empty result table is ordinary SQL" },
                { "result-read-unknown-call-bracket.json", "lint-only: result-read-unknown-call is an authoring rule; reading an empty result table is ordinary SQL" },
                { "result-read-unknown-call.json", "lint-only: result-read-unknown-call is an authoring rule; reading an empty result table is ordinary SQL" },
                { "sqlite-version-too-low-for-function-if.json", "lint-only: the code is a portability warning about the AUTHOR's floor; the engine under test decides whether the function exists" },
                { "sqlite-version-too-low-for-function-pragma-table-list.json", "lint-only: the code is a portability warning about the AUTHOR's floor; the engine under test decides whether the function exists" },
                { "sqlite-version-too-low-for-function-quoted.json", "lint-only: the code is a portability warning about the AUTHOR's floor; the engine under test decides whether the function exists" },
                { "sqlite-version-too-low-for-function.json", "lint-only: the code is a portability warning about the AUTHOR's floor; the engine under test decides whether the function exists" },
                { "undeclared-method-use-bracket.json", "lint-only: undeclared-method-use is an authoring rule; requiredMethods is a claim the runtime checks, not an allow-list" },
                { "undeclared-method-use.json", "lint-only: undeclared-method-use is an authoring rule; requiredMethods is a claim the runtime checks, not an allow-list" },
                { "unknown-column.json", "post-envelope: SQLite refuses to prepare the statement" },
                { "unrecognized-statement.json", "post-envelope: SQLite refuses to prepare the statement" },
                { "unused-binding.json", "post-envelope: refused at bind as unused-binding (the conformance suite owns that)" },
            };
        // <<< invalid-not-envelope

        /// <summary>Every <c>*.json</c> in a payload directory, sorted, name only.</summary>
        internal static IReadOnlyList<string> ListPayloads(string directory)
        {
            var files = new List<string>();
            foreach (string path in Directory.GetFiles(
                Path.Combine(FixturePaths.Root, "payloads", directory), "*.json"))
            {
                files.Add(Path.GetFileName(path));
            }
            files.Sort(StringComparer.Ordinal);
            return files;
        }
    }
}
