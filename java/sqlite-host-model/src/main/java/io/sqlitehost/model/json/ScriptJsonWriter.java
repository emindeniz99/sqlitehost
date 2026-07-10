package io.sqlitehost.model.json;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import io.sqlitehost.model.envelope.BindingValue;
import io.sqlitehost.model.envelope.RuntimeInput;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.envelope.Statement;
import io.sqlitehost.model.envelope.Step;

import java.util.Base64;
import java.util.Map;

/**
 * JSON writer for the script envelope — the inverse of
 * {@link ScriptJsonReader}. Wire rules per docs/script-envelope.md:
 * {@code int64} is written as a JSON number when |v| &le; 2^53−1 and as
 * a decimal string otherwise; {@code blob} as standard padded base64;
 * the {@code null} binding carries no value. Optional envelope fields
 * are omitted when null/empty; {@code bindings} is always written
 * (possibly empty) to match the fixture payloads.
 */
public final class ScriptJsonWriter {

    /** Largest int64 magnitude representable exactly as a JSON number (2^53−1). */
    private static final long MAX_SAFE_JSON_INTEGER = 9007199254740991L;

    private static final ObjectMapper MAPPER = new ObjectMapper();

    private ScriptJsonWriter() {
    }

    public static String write(Script script) {
        try {
            return MAPPER.writerWithDefaultPrettyPrinter().writeValueAsString(toTree(script));
        } catch (com.fasterxml.jackson.core.JsonProcessingException e) {
            // Building from an in-memory tree cannot fail to serialize.
            throw new IllegalStateException("failed to serialize script envelope", e);
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
        }
        return node;
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
