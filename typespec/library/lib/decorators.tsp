using TypeSpec.Reflection;

namespace SqliteHost;

/**
 * Options for `@hostLibrary`. `apiLevel` is required; the six naming
 * keys are optional and default to the protocol v1 naming conventions
 * (see docs/naming.md).
 */
model HostLibraryOptions {
  apiLevel: int32;
  callTablePrefix?: string;
  resultTablePrefix?: string;
  inputColumnPrefix?: string;
  resultColumnPrefix?: string;
  inputListTableInfix?: string;
  resultListTableInfix?: string;
}

/**
 * Options for `@hostMethod`. `name` is the logical protocol method name
 * (e.g. "getValue"); `handler` is the member name on the generated
 * handler interface. `apiLevel` defaults to the library's api level.
 */
model HostMethodOptions {
  name: string;
  handler: string;
  apiLevel?: int32;
}

/**
 * Marks an interface as a SqliteHost host library. Every operation in
 * the interface must carry `@hostMethod`.
 */
extern dec hostLibrary(target: Interface, options: HostLibraryOptions);

/**
 * Declares a host method binding for an operation inside a
 * `@hostLibrary` interface.
 */
extern dec hostMethod(target: Operation, options: HostMethodOptions);

/**
 * Overrides the logical SQL name of a model property. Defaults to
 * snake_case of the property name when absent.
 */
extern dec sqlName(target: ModelProperty, name: valueof string);
