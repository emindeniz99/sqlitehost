package io.sqlitehost.model.json;

import com.fasterxml.jackson.core.JsonFactory;
import com.fasterxml.jackson.core.JsonGenerator;
import com.fasterxml.jackson.core.json.JsonWriteFeature;
import com.fasterxml.jackson.core.util.DefaultIndenter;
import com.fasterxml.jackson.core.util.DefaultPrettyPrinter;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.fasterxml.jackson.databind.util.RawValue;
import io.sqlitehost.model.envelope.BindingValue;
import io.sqlitehost.model.envelope.RuntimeInput;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.envelope.Statement;
import io.sqlitehost.model.envelope.Step;

import java.io.IOException;
import java.util.Base64;
import java.util.Map;

/**
 * JSON writer for the script envelope — the inverse of
 * {@link ScriptJsonReader}. Wire rules per docs/script-envelope.md:
 * {@code int64} is written as a JSON number when |v| &le; 2^53−1 and as
 * a decimal string otherwise; {@code blob} as standard padded base64;
 * floats in the canonical text {@link CanonicalFloatText} defines; the
 * {@code null} binding carries no value. Optional envelope fields are
 * omitted when null/empty; {@code bindings} is always written (possibly
 * empty) to match the fixture payloads.
 *
 * <p>The output is <em>canonical bytes</em>, not merely equivalent JSON:
 * pinned key order, two-space indentation, LF newlines and a trailing
 * one, exactly what {@code JSON.stringify(script, null, 2) + "\n"}
 * produces in {@code @sqlite-host/runtime-types}. An envelope is signed
 * bytes, so re-writing a canonical payload has to reproduce it verbatim
 * rather than something a parser would call the same document.
 */
public final class ScriptJsonWriter {

    /** Largest int64 magnitude representable exactly as a JSON number (2^53−1). */
    private static final long MAX_SAFE_JSON_INTEGER = 9007199254740991L;

    /**
     * Lower-case {@code \}{@code u000b} escapes, the spelling
     * {@code JSON.stringify} emits; Jackson defaults to upper case.
     */
    private static final ObjectMapper MAPPER = new ObjectMapper(
            JsonFactory.builder()
                    .disable(JsonWriteFeature.WRITE_HEX_UPPER_CASE)
                    .build());

    private ScriptJsonWriter() {
    }

    public static String write(Script script) {
        try {
            return MAPPER.writer(new CanonicalPrettyPrinter())
                    .writeValueAsString(toTree(script)) + "\n";
        } catch (com.fasterxml.jackson.core.JsonProcessingException e) {
            // Building from an in-memory tree cannot fail to serialize.
            throw new IllegalStateException("failed to serialize script envelope", e);
        }
    }

    /**
     * {@code JSON.stringify(value, null, 2)} layout: two-space indent on
     * objects <em>and</em> arrays, LF, {@code ": "} between key and
     * value, and empty containers kept on one line as {@code {}} /
     * {@code []}. Jackson's own default printer differs on every one of
     * those points, which is why this exists.
     */
    private static final class CanonicalPrettyPrinter extends DefaultPrettyPrinter {

        private static final long serialVersionUID = 1L;

        CanonicalPrettyPrinter() {
            DefaultIndenter indenter = new DefaultIndenter("  ", "\n");
            indentObjectsWith(indenter);
            indentArraysWith(indenter);
        }

        @Override
        public DefaultPrettyPrinter createInstance() {
            return new CanonicalPrettyPrinter();
        }

        @Override
        public void writeObjectFieldValueSeparator(JsonGenerator gen) throws IOException {
            gen.writeRaw(": ");
        }

        @Override
        public void writeEndObject(JsonGenerator gen, int nrOfEntries) throws IOException {
            if (nrOfEntries == 0) {
                // Jackson would write "{ }"; JSON.stringify writes "{}".
                // The depth still has to come back down, as it does in
                // the superclass — skipping it indents the rest of the
                // document one level too deep.
                --_nesting;
                gen.writeRaw('}');
                return;
            }
            super.writeEndObject(gen, nrOfEntries);
        }

        @Override
        public void writeEndArray(JsonGenerator gen, int nrOfValues) throws IOException {
            if (nrOfValues == 0) {
                --_nesting;
                gen.writeRaw(']');
                return;
            }
            super.writeEndArray(gen, nrOfValues);
        }
    }

    private static ObjectNode toTree(Script script) {
        ObjectNode root = MAPPER.createObjectNode();
        if (script.engine() != null) {
            root.put("engine", script.engine());
        }
        if (script.scriptId() != null) {
            root.put("scriptId", script.scriptId());
        }
        if (script.requiredApiLevel() != null) {
            root.put("requiredApiLevel", script.requiredApiLevel());
        }
        putStringArray(root, "requiredFeatures", script.requiredFeatures());
        putStringArray(root, "requiredMethods", script.requiredMethods());
        if (!script.inputs().isEmpty()) {
            ArrayNode inputs = root.putArray("inputs");
            for (RuntimeInput input : script.inputs()) {
                ObjectNode inputNode = inputs.addObject();
                inputNode.put("name", input.name());
                if (input.value() != null) {
                    inputNode.set("value", bindingValueNode(input.value()));
                }
            }
        }
        ArrayNode steps = root.putArray("steps");
        for (Step step : script.steps()) {
            ObjectNode stepNode = steps.addObject();
            if (step.id() != null) {
                stepNode.put("id", step.id());
            }
            ArrayNode statements = stepNode.putArray("statements");
            for (Statement statement : step.statements()) {
                ObjectNode statementNode = statements.addObject();
                if (statement.sql() != null) {
                    statementNode.put("sql", statement.sql());
                }
                ObjectNode bindings = statementNode.putObject("bindings");
                for (Map.Entry<String, BindingValue> binding : statement.bindings().entrySet()) {
                    bindings.set(binding.getKey(), bindingValueNode(binding.getValue()));
                }
            }
        }
        return root;
    }

    private static ObjectNode bindingValueNode(BindingValue value) {
        ObjectNode node = MAPPER.createObjectNode();
        node.put("type", value.type().jsonName());
        switch (value.type()) {
            case NULL:
                break;
            case INT32:
                node.put("value", value.asInt32());
                break;
            case INT64: {
                long v = value.asInt64();
                if (v >= -MAX_SAFE_JSON_INTEGER && v <= MAX_SAFE_JSON_INTEGER) {
                    node.put("value", v);
                } else {
                    node.put("value", Long.toString(v));
                }
                break;
            }
            case BOOL:
                node.put("value", value.asBool());
                break;
            case TEXT:
                node.put("value", value.asText());
                break;
            case BLOB:
                node.put("value", Base64.getEncoder().encodeToString(value.asBlob()));
                break;
            case FLOAT32:
                // Floats are always JSON numbers (no string form); a
                // float32 is written via its exact double value.
                putFloat(node, (double) value.asFloat32());
                break;
            case FLOAT64:
                putFloat(node, value.asFloat64());
                break;
        }
        return node;
    }

    /**
     * Writes the number as raw canonical text. A {@code DoubleNode}
     * would go through {@code Double.toString}, whose digits differ from
     * the contract's and from one JDK to the next — see
     * {@link CanonicalFloatText}.
     */
    private static void putFloat(ObjectNode node, double value) {
        node.putRawValue("value", new RawValue(CanonicalFloatText.of(value)));
    }

    private static void putStringArray(ObjectNode parent, String field, Iterable<String> values) {
        boolean any = false;
        for (String ignored : values) {
            any = true;
            break;
        }
        if (!any) {
            return;
        }
        ArrayNode array = parent.putArray(field);
        for (String value : values) {
            array.add(value);
        }
    }
}
