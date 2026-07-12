package io.sqlitehost.model.manifest;

/**
 * One host method descriptor with resolved physical names
 * (call/result tables, queue trigger) and input/result shapes.
 * {@code mutates} is the conservative eligibility flag for inline
 * exposure (defaulted to {@code true} at generation time); {@code
 * inline} is {@code null} for methods without an inline scalar
 * function (docs/proposals/inline-host-functions.md).
 */
public record MethodDescriptor(
        String operationName,
        String methodName,
        String handlerName,
        int apiLevel,
        boolean mutates,
        String callTable,
        String resultTable,
        String queueTrigger,
        ObjectShape input,
        ObjectShape result,
        InlineFunction inline) {
}
