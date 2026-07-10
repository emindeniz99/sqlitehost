package io.sqlitehost.model.manifest;

/**
 * The manifest {@code columns} block (docs/naming.md): every shared
 * SQL-visible column name plus the done-status literal, resolved per
 * host. Fields mirror the manifest keys exactly, in manifest order.
 */
public record ManifestColumns(
        String callId,
        String itemIndex,
        String status,
        String doneValue,
        String queueId,
        String method,
        String name,
        String valueType,
        String intValue,
        String realValue,
        String textValue,
        String blobValue,
        String action,
        String message) {
}
