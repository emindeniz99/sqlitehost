package io.sqlitehost.model.manifest;

import java.util.Collections;
import java.util.List;

/** The manifest {@code library} block. */
public record ManifestLibrary(
        String namespace,
        String interfaceName,
        int apiLevel,
        List<String> features) {

    public ManifestLibrary {
        features = features == null ? Collections.emptyList() : List.copyOf(features);
    }
}
