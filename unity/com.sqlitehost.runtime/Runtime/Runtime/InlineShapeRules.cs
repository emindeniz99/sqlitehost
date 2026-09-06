using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// The inline eligibility shape rules
    /// (docs/proposals/inline-host-functions.md), shared by every
    /// registration surface so classic, compact, and ultra specs fail loud
    /// with identical messages: scalar-only input, exactly one scalar
    /// result, no lists, optional input fields trailing. Returns null when
    /// the method is not inline-exposed.
    ///
    /// The arity is a manifest value: generated code passes the IR's
    /// minArgs/maxArgs straight through, so the C# runtime does not carry
    /// a second copy of the rule that produced them
    /// (codegen/core/src/frontend.ts). Deriving it from the declared field
    /// shapes is the fallback for hand-written definitions, which have no
    /// manifest to read it from — and InlineArityTests pins the two
    /// against each other on the sample host so they cannot part ways.
    /// </summary>
    internal static class InlineShapeRules
    {
        /// <summary>Arity sentinel for a hand-written spec: derive it instead.</summary>
        internal const int NotDeclared = -1;

        public static InlineFunctionModel BuildModel(
            string methodName,
            string inlineFunctionName,
            IReadOnlyList<ErasedReadField> inputFields,
            int inputListFieldCount,
            int resultFieldCount,
            int resultListFieldCount,
            int declaredMinArgs = NotDeclared,
            int declaredMaxArgs = NotDeclared)
        {
            if (inlineFunctionName == null)
            {
                return null;
            }
            if (inputListFieldCount > 0)
            {
                throw new InvalidOperationException(
                    "Method '" + methodName + "' cannot be exposed as inline function '"
                    + inlineFunctionName + "': the input must have scalar fields only (no lists).");
            }
            if (resultListFieldCount > 0)
            {
                throw new InvalidOperationException(
                    "Method '" + methodName + "' cannot be exposed as inline function '"
                    + inlineFunctionName + "': the result must have scalar fields only (no lists).");
            }
            if (resultFieldCount != 1)
            {
                throw new InvalidOperationException(
                    "Method '" + methodName + "' cannot be exposed as inline function '"
                    + inlineFunctionName + "': the result must have exactly one scalar field (found "
                    + resultFieldCount + ").");
            }
            int requiredCount = 0;
            bool sawOptional = false;
            foreach (ErasedReadField field in inputFields)
            {
                if (field.Optional)
                {
                    sawOptional = true;
                    continue;
                }
                if (sawOptional)
                {
                    throw new InvalidOperationException(
                        "Method '" + methodName + "' cannot be exposed as inline function '"
                        + inlineFunctionName + "': required input field '" + field.SqlName
                        + "' is declared after an optional field (optional fields must be trailing).");
                }
                requiredCount++;
            }
            return new InlineFunctionModel(
                inlineFunctionName,
                declaredMinArgs == NotDeclared ? requiredCount : declaredMinArgs,
                declaredMaxArgs == NotDeclared ? inputFields.Count : declaredMaxArgs);
        }
    }
}
