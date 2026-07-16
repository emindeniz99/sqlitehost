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
        public static void RequireReferenceDtoTypes(Type inputType, Type resultType, string methodName)
        {
            if (inputType.IsValueType || resultType.IsValueType)
            {
                throw new ArgumentException(
                    "Method '" + methodName + "': input and result DTO types must be classes"
                    + " (value-type DTOs are not supported).",
                    "methodName");
            }
        }

        public static void RequireReferenceItemType(Type itemType, string sqlName)
        {
            if (itemType.IsValueType)
            {
                throw new ArgumentException(
                    "List field '" + sqlName + "': item DTO types must be classes"
                    + " (value-type items are not supported).",
                    "sqlName");
            }
        }
    }
}
