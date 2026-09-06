/**
 * Target-language reserved words.
 *
 * `IDENTIFIER` is the TypeSpec/SQL identifier shape, and a name that
 * satisfies it can still be a keyword in a language the emitters write.
 * No emitter escapes or prefixes one: the C# emitter interpolates the
 * handler name raw into an interface member and a call site (it has no
 * `@`-verbatim path), and the Java emitter lowercases the library
 * namespace straight into a `package` declaration. Neither language has
 * a spelling that rescues the result — Java in particular has no escape
 * at all for a keyword package segment — so rejecting the name at
 * authoring time is the only uniform answer.
 *
 * These live here, beside the identifier patterns the decorators
 * enforce, because both the decorator (`$hostMethod`) and the manifest
 * validator in codegen-core have to apply the same sets, and
 * codegen-core can import this package while the reverse would form a
 * workspace cycle.
 */

/**
 * C# reserved keywords (C# language reference, "Keywords"). Contextual
 * keywords (`record`, `var`, `value`, `partial`, `when`, `yield`, the
 * LINQ query words, …) are deliberately absent: they are legal
 * identifiers, so rejecting them would refuse names that compile.
 */
export const CSHARP_RESERVED_WORDS: readonly string[] = [
  "abstract",
  "as",
  "base",
  "bool",
  "break",
  "byte",
  "case",
  "catch",
  "char",
  "checked",
  "class",
  "const",
  "continue",
  "decimal",
  "default",
  "delegate",
  "do",
  "double",
  "else",
  "enum",
  "event",
  "explicit",
  "extern",
  "false",
  "finally",
  "fixed",
  "float",
  "for",
  "foreach",
  "goto",
  "if",
  "implicit",
  "in",
  "int",
  "interface",
  "internal",
  "is",
  "lock",
  "long",
  "namespace",
  "new",
  "null",
  "object",
  "operator",
  "out",
  "override",
  "params",
  "private",
  "protected",
  "public",
  "readonly",
  "ref",
  "return",
  "sbyte",
  "sealed",
  "short",
  "sizeof",
  "stackalloc",
  "static",
  "string",
  "struct",
  "switch",
  "this",
  "throw",
  "true",
  "try",
  "typeof",
  "uint",
  "ulong",
  "unchecked",
  "unsafe",
  "ushort",
  "using",
  "virtual",
  "void",
  "volatile",
  "while",
];

/**
 * Java keywords (JLS §3.9), plus the three reserved literals
 * (`true`/`false`/`null`) and the lone underscore reserved since Java 9
 * — none of them may be an identifier. Restricted identifiers (`var`,
 * `record`, `sealed`, `permits`, `yield`, the module-declaration words)
 * are absent for the same reason C#'s contextual keywords are: they
 * compile fine as ordinary names.
 */
export const JAVA_RESERVED_WORDS: readonly string[] = [
  "_",
  "abstract",
  "assert",
  "boolean",
  "break",
  "byte",
  "case",
  "catch",
  "char",
  "class",
  "const",
  "continue",
  "default",
  "do",
  "double",
  "else",
  "enum",
  "extends",
  "false",
  "final",
  "finally",
  "float",
  "for",
  "goto",
  "if",
  "implements",
  "import",
  "instanceof",
  "int",
  "interface",
  "long",
  "native",
  "new",
  "null",
  "package",
  "private",
  "protected",
  "public",
  "return",
  "short",
  "static",
  "strictfp",
  "super",
  "switch",
  "synchronized",
  "this",
  "throw",
  "throws",
  "transient",
  "true",
  "try",
  "void",
  "volatile",
  "while",
];

const CSHARP_SET: ReadonlySet<string> = new Set(CSHARP_RESERVED_WORDS);
const JAVA_SET: ReadonlySet<string> = new Set(JAVA_RESERVED_WORDS);

/** Both languages are case-sensitive, so `Class` is a legal name in each. */
export function isCSharpReservedWord(name: string): boolean {
  return CSHARP_SET.has(name);
}

export function isJavaReservedWord(name: string): boolean {
  return JAVA_SET.has(name);
}

/**
 * The languages in which `name` cannot be used, as a display list for a
 * diagnostic. `javaForm` is the spelling Java actually receives when it
 * differs from the authored one — the Java emitter lowercases the
 * library namespace into its `package` declaration, so the segment
 * `New` reaches javac as `new` while C# keeps `New` and is fine.
 * Returns an empty array when the name is usable in both.
 */
export function reservedWordLanguages(
  name: string,
  javaForm: string = name,
): string[] {
  const languages: string[] = [];
  if (isCSharpReservedWord(name)) {
    languages.push("C#");
  }
  if (isJavaReservedWord(javaForm)) {
    languages.push("Java");
  }
  return languages;
}
