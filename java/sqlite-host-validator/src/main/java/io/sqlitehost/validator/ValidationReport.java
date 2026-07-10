package io.sqlitehost.validator;

import java.util.Collections;
import java.util.List;

/**
 * The findings of one validation run. A payload is publishable when it
 * has zero errors; warnings don't block (docs/validation.md).
 */
public record ValidationReport(List<ValidationFinding> findings) {

    public ValidationReport {
        findings = findings == null ? Collections.emptyList() : List.copyOf(findings);
    }

    /** True when the report contains no error-severity findings. */
    public boolean isValid() {
        for (ValidationFinding finding : findings) {
            if (finding.severity() == Severity.ERROR) {
                return false;
            }
        }
        return true;
    }

    public List<ValidationFinding> errors() {
        return findings.stream().filter(f -> f.severity() == Severity.ERROR).toList();
    }

    public List<ValidationFinding> warnings() {
        return findings.stream().filter(f -> f.severity() == Severity.WARNING).toList();
    }
}
