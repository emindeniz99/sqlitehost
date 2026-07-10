package io.sqlitehost.model.manifest;

import java.util.Collections;
import java.util.List;

/**
 * The canonical manifest — serialized IR (docs/manifest.md). Mirrors
 * the JSON committed at fixtures/manifests exactly; all physical names
 * are resolved.
 */
public record Manifest(
        int manifestVersion,
        String engine,
        ManifestLibrary library,
        ManifestNaming naming,
        ManifestTable queueTable,
        ManifestTable inputsTable,
        ManifestTable varsTable,
        ScriptEnvelopeDescriptor scriptEnvelope,
        List<MethodDescriptor> methods) {

    public Manifest {
        methods = methods == null ? Collections.emptyList() : List.copyOf(methods);
    }

    /** Find a method by protocol name, or {@code null} when absent. */
    public MethodDescriptor methodByName(String methodName) {
        for (MethodDescriptor method : methods) {
            if (method.methodName().equals(methodName)) {
                return method;
            }
        }
        return null;
    }
}
