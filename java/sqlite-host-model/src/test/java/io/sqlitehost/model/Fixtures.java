package io.sqlitehost.model;

import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;

/** Locates the shared fixtures/ directory by walking up from the module dir. */
public final class Fixtures {

    private Fixtures() {
    }

    public static Path fixturesDir() {
        Path dir = Paths.get("").toAbsolutePath();
        while (dir != null) {
            Path candidate = dir.resolve("fixtures");
            if (Files.isRegularFile(
                    candidate.resolve("manifests").resolve("sample-host.manifest.json"))) {
                return candidate;
            }
            dir = dir.getParent();
        }
        throw new IllegalStateException(
                "could not locate the fixtures/ directory above " + Paths.get("").toAbsolutePath());
    }
}
