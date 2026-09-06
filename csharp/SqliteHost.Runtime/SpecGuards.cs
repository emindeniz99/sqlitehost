using System;

namespace SqliteHost
{
    /// <summary>
    /// Fail-loud guards shared by the spec builders. The erased execution
    /// core passes DTOs around boxed, so a value-type DTO would silently
    /// mutate an unboxed copy — rejected at registration time instead.
    /// </summary>
    internal static class SpecGuards
    {
        /// <summary>
        /// A resolved-name side channel (<c>Column</c> / <c>ChildTable</c>)
        /// names the declaration immediately before it, so calling one with
        /// nothing declared is a coding error, not a name to drop silently.
        /// Unlike the DTO guards this is NOT stripped under SQLITEHOST_SLIM:
        /// dropping the name would leave the runtime deriving a column the
        /// generated schema never created.
        /// </summary>
        public static void RequireDeclarationBefore(bool declared, string call)
        {
            if (!declared)
            {
                throw new InvalidOperationException(
                    call + "(...) applies to the declaration before it; nothing has been declared yet.");
            }
        }

        public static void RequireReferenceDtoTypes(Type inputType, Type resultType, string methodName)
        {
#if !SQLITEHOST_SLIM
            if (inputType.IsValueType || resultType.IsValueType)
            {
                throw new ArgumentException(
                    "Method '" + methodName + "': input and result DTO types must be classes"
                    + " (value-type DTOs are not supported).",
                    "methodName");
            }
#endif
        }

        public static void RequireReferenceItemType(Type itemType, string sqlName)
        {
#if !SQLITEHOST_SLIM
            if (itemType.IsValueType)
            {
                throw new ArgumentException(
                    "List field '" + sqlName + "': item DTO types must be classes"
                    + " (value-type items are not supported).",
                    "sqlName");
            }
#endif
        }
    }
}
