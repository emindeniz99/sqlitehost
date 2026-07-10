package io.sqlitehost.model;

import example.game.generated.GetValueInput;
import example.game.generated.GetValuesInput;
import example.game.generated.GetValuesResult;
import example.game.generated.KeyQueryItem;
import example.game.generated.MethodDescriptors;
import example.game.generated.PutBlobInput;
import example.game.generated.ValueEntryItem;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.manifest.ListField;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.model.manifest.MethodDescriptor;
import io.sqlitehost.model.manifest.ScalarField;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

/**
 * The generated sample package (example.game.generated, emitted by
 * codegen/java-emitter from the sample manifest) must stay usable as
 * plain Java: DTO records hold the mapped field types, and
 * MethodDescriptors mirrors the committed manifest metadata. The
 * byte-level goldens live in codegen/java-emitter's node tests; this
 * test pins the Java-side semantics.
 */
class GeneratedSampleTest {

    @Test
    void dtoRecordsCarryMappedScalarTypes() {
        GetValueInput input = new GetValueInput("hero:hp");
        assertEquals("hero:hp", input.key());

        // Optional string stays a nullable String; bytes map to byte[].
        PutBlobInput blob = new PutBlobInput("icon", new byte[] {1, 2}, null);
        assertEquals(2, blob.data().length);
        assertNull(blob.note());
    }

    @Test
    void listFieldsAreDefensivelyCopiedAndNullBecomesEmpty() {
        List<KeyQueryItem> keys = new ArrayList<>();
        keys.add(new KeyQueryItem("a"));
        GetValuesInput input = new GetValuesInput(null, keys);
        keys.add(new KeyQueryItem("b"));

        // Optional int64 is a boxed Long; the list snapshot is immutable.
        assertNull(input.defaultValue());
        assertEquals(1, input.keys().size());
        assertThrows(UnsupportedOperationException.class,
                () -> input.keys().add(new KeyQueryItem("c")));

        GetValuesResult result = new GetValuesResult(null);
        assertEquals(List.of(), result.entries());
        assertEquals(new ValueEntryItem("a", 7L, true),
                new ValueEntryItem("a", 7L, true));
    }

    @Test
    void methodDescriptorsMirrorTheCommittedManifest() throws IOException {
        Manifest manifest = ManifestJsonReader.read(Files.readString(
                Fixtures.fixturesDir().resolve("manifests/sample-host.manifest.json")));

        assertEquals(manifest.engine(), MethodDescriptors.ENGINE);
        assertEquals(manifest.library().namespace(), MethodDescriptors.NAMESPACE);
        assertEquals(manifest.library().interfaceName(), MethodDescriptors.INTERFACE_NAME);
        assertEquals(manifest.library().apiLevel(), MethodDescriptors.API_LEVEL);

        assertEquals(manifest.methods().size(), MethodDescriptors.ALL.size());
        for (int i = 0; i < manifest.methods().size(); i++) {
            MethodDescriptor expected = manifest.methods().get(i);
            MethodDescriptors.Method actual = MethodDescriptors.ALL.get(i);
            assertEquals(expected.methodName(), actual.methodName());
            assertEquals(expected.handlerName(), actual.handlerName());
            assertEquals(expected.apiLevel(), actual.apiLevel());
            assertEquals(expected.callTable(), actual.callTable());
            assertEquals(expected.resultTable(), actual.resultTable());
            assertEquals(expected.queueTrigger(), actual.queueTrigger());
            assertEquals(expected.input().fields().stream().map(ScalarField::column).toList(),
                    actual.inputColumns());
            assertEquals(expected.input().listFields().stream().map(ListField::childTable).toList(),
                    actual.inputListTables());
            assertEquals(expected.result().fields().stream().map(ScalarField::column).toList(),
                    actual.resultColumns());
            assertEquals(expected.result().listFields().stream().map(ListField::childTable).toList(),
                    actual.resultListTables());
        }
    }
}
