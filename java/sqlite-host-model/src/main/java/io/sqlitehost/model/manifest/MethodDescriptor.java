package io.sqlitehost.model.manifest;

/**
 * One host method descriptor with resolved physical names
 * (call/result tables, queue trigger) and input/result shapes.
 */
public record MethodDescriptor(
        String operationName,
        String methodName,
        String handlerName,
        int apiLevel,
        String callTable,
        String resultTable,
        String queueTrigger,
        ObjectShape input,
        ObjectShape result) {
}
