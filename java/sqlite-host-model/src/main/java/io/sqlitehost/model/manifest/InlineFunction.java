package io.sqlitehost.model.manifest;

import java.util.Collections;
import java.util.List;

/**
 * Inline scalar-function exposure of a non-mutating method (feature
 * {@code inlineFunctions} — docs/proposals/inline-host-functions.md).
 * Mirrors the manifest JSON exactly: {@code functionName} is resolved
 * ({@code functionPrefix + snake(methodName)} unless overridden), and
 * the function registers every arity in {@code minArgs..maxArgs}
 * (optional trailing arguments may be omitted).
 */
public record InlineFunction(
        String functionName,
        int minArgs,
        int maxArgs,
        List<InlineArg> args,
        InlineReturn returns) {

    public InlineFunction {
        args = args == null ? Collections.emptyList() : List.copyOf(args);
    }
}
