package io.sqlitehost.validator;

/**
 * One validation finding. {@code stepId} is {@code null} and
 * {@code statementIndex} is {@code -1} for findings that are not
 * scoped to a statement (mirrors the runtime failure-context rule in
 * docs/errors.md).
 */
public record ValidationFinding(
        String code,
        Severity severity,
        String stepId,
        int statementIndex,
        String message) {

    public static ValidationFinding error(String code, String message) {
        return new ValidationFinding(code, Severity.ERROR, null, -1, message);
    }

    public static ValidationFinding error(
            String code, String stepId, int statementIndex, String message) {
        return new ValidationFinding(code, Severity.ERROR, stepId, statementIndex, message);
    }

    public static ValidationFinding warning(String code, String message) {
        return new ValidationFinding(code, Severity.WARNING, null, -1, message);
    }

    public static ValidationFinding warning(
            String code, String stepId, int statementIndex, String message) {
        return new ValidationFinding(code, Severity.WARNING, stepId, statementIndex, message);
    }

    /** One-line rendering: {@code ERROR missing-binding [read/0] …}. */
    public String render() {
        StringBuilder line = new StringBuilder();
        line.append(severity).append(' ').append(code);
        if (stepId != null || statementIndex >= 0) {
            line.append(" [")
                    .append(stepId == null ? "?" : stepId);
            if (statementIndex >= 0) {
                line.append('/').append(statementIndex);
            }
            line.append(']');
        }
        line.append(' ').append(message);
        return line.toString();
    }
}
