package io.sqlitehost.model.manifest;

import java.util.Collections;
import java.util.List;

/** The manifest {@code scriptEnvelope} block. */
public record ScriptEnvelopeDescriptor(String engine, List<String> bindingTypes) {

    public ScriptEnvelopeDescriptor {
        bindingTypes = bindingTypes == null ? Collections.emptyList() : List.copyOf(bindingTypes);
    }
}
