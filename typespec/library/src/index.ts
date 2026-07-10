export { $lib, reportDiagnostic, createDiagnostic, stateKeys } from "./lib.js";
// Note: $hostLibrary/$hostMethod/$sqlName are deliberately NOT re-exported
// here — the compiler would treat top-level `$`-prefixed exports as legacy
// global-namespace decorator implementations, clashing with $decorators.
export {
  getHostLibraryOptions,
  getHostLibraryInterfaces,
  getHostMethodOptions,
  getSqlName,
  parseSqliteVersionNumber,
} from "./decorators.js";
export type { HostLibraryOptions, HostMethodOptions } from "./decorators.js";

import { $hostLibrary, $hostMethod, $sqlName } from "./decorators.js";

/** Decorator implementations, keyed by TypeSpec namespace. */
export const $decorators = {
  SqliteHost: {
    hostLibrary: $hostLibrary,
    hostMethod: $hostMethod,
    sqlName: $sqlName,
  },
};
