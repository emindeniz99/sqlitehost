package io.sqlitehost.model.json;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.sqlitehost.model.envelope.BindingValue;
import io.sqlitehost.model.envelope.RuntimeInput;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.envelope.Statement;
import io.sqlitehost.model.envelope.Step;

import java.io.IOException;
import java.io.InputStream;
import java.math.BigInteger;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.regex.Pattern;

/**
 * Strict JSON reader for the script envelope (docs/script-envelope.md).
 *
 * <p>Strictness rules: field types must match the contract, an unknown
 * envelope binding {@code type} is an error, {@code int32}/{@code int64}
 * accept a JSON number or a decimal string (with range checks),
 * {@code float32}/{@code float64} accept a finite JSON number only, and
 * {@code blob} must be valid base64. Structural rules that the semantic
 * validator owns (missing engine, empty steps, duplicate step ids, …)
 * are deliberately NOT enforced here — the validator reports them as
 * findings on the parsed script.</p>
 */
public final class ScriptJsonReader {

    private static final ObjectMapper MAPPER = new ObjectMapper();
    /** Largest int64 magnitude representable exactly as a JSON number (2^53−1), mirrors ScriptJsonWriter. */
    private static final BigInteger MAX_SAFE_JSON_INTEGER = BigInteger.valueOf(9007199254740991L);
    /** Strict base64 (docs/script-envelope.md): standard alphabet, padded, no whitespace. */
    private static final Pattern BASE64 = Pattern.compile(
            "^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$");
    /** Strict decimal string: no whitespace, no leading '+'. */
    private static final Pattern DECIMAL_STRING = Pattern.compile("^-?[0-9]+$");

    private ScriptJsonReader() {
    }

    public static Script read(String json) throws IOException {
        return fromTree(MAPPER.readTree(json));
    }

    public static Script read(InputStream in) throws IOException {
        return fromTree(MAPPER.readTree(in));
    }

    private static Script fromTree(JsonNode root) throws JsonReadException {
        if (root == null || !root.isObject()) {
            throw new JsonReadException("script envelope must be a JSON object");
        }
        String engine = optionalString(root, "engine");
        String scriptId = optionalString(root, "scriptId");
        Integer requiredApiLevel = optionalInt(root, "requiredApiLevel");
        List<String> requiredFeatures = stringList(root, "requiredFeatures");
        List<String> requiredMethods = stringList(root, "requiredMethods");
        List<RuntimeInput> inputs = readInputs(root.get("inputs"));
        List<Step> steps = readSteps(root.get("steps"));
        return new Script(engine, scriptId, requiredApiLevel,
                requiredFeatures, requiredMethods, inputs, steps);
    }

    private static List<RuntimeInput> readInputs(JsonNode node) throws JsonReadException {
        List<RuntimeInput> inputs = new ArrayList<>();
        if (node == null || node.isNull()) {
            return inputs;
        }
        if (!node.isArray()) {
            throw new JsonReadException("inputs must be an array");
        }
        for (JsonNode entry : node) {
            if (!entry.isObject()) {
                throw new JsonReadException("inputs entries must be objects");
            }
            String name = optionalString(entry, "name");
            JsonNode value = entry.get("value");
            BindingValue bindingValue = value == null || value.isNull()
                    ? null
                    : readBindingValue(value, "input '" + name + "'");
            inputs.add(new RuntimeInput(name, bindingValue));
        }
        return inputs;
    }

    private static List<Step> readSteps(JsonNode node) throws JsonReadException {
        List<Step> steps = new ArrayList<>();
        if (node == null || node.isNull()) {
            return steps;
        }
        if (!node.isArray()) {
            throw new JsonReadException("steps must be an array");
        }
        for (JsonNode stepNode : node) {
            if (!stepNode.isObject()) {
                throw new JsonReadException("steps entries must be objects");
            }
            String id = optionalString(stepNode, "id");
            List<Statement> statements = readStatements(stepNode.get("statements"), id);
            steps.add(new Step(id, statements));
        }
        return steps;
    }

    private static List<Statement> readStatements(JsonNode node, String stepId)
            throws JsonReadException {
        List<Statement> statements = new ArrayList<>();
        if (node == null || node.isNull()) {
            return statements;
        }
        if (!node.isArray()) {
            throw new JsonReadException("statements must be an array (step '" + stepId + "')");
        }
        for (JsonNode statementNode : node) {
            if (!statementNode.isObject()) {
                throw new JsonReadException(
                        "statements entries must be objects (step '" + stepId + "')");
            }
            String sql = optionalString(statementNode, "sql");
            Map<String, BindingValue> bindings =
                    readBindings(statementNode.get("bindings"), stepId);
            statements.add(new Statement(sql, bindings));
        }
        return statements;
    }

    private static Map<String, BindingValue> readBindings(JsonNode node, String stepId)
            throws JsonReadException {
        Map<String, BindingValue> bindings = new LinkedHashMap<>();
        if (node == null || node.isNull()) {
            return bindings;
        }
        if (!node.isObject()) {
            throw new JsonReadException("bindings must be an object (step '" + stepId + "')");
        }
        Iterator<Map.Entry<String, JsonNode>> fields = node.fields();
        while (fields.hasNext()) {
            Map.Entry<String, JsonNode> field = fields.next();
            bindings.put(field.getKey(),
                    readBindingValue(field.getValue(), "binding '" + field.getKey() + "'"));
        }
        return bindings;
    }

    /** Read one discriminated binding value object ({type, value}). */
    static BindingValue readBindingValue(JsonNode node, String context)
            throws JsonReadException {
        if (node == null || !node.isObject()) {
            throw new JsonReadException(context + ": binding value must be an object");
        }
        JsonNode typeNode = node.get("type");
        if (typeNode == null || !typeNode.isTextual()) {
            throw new JsonReadException(context + ": binding value is missing its type");
        }
        BindingValue.Type type = BindingValue.Type.fromJsonName(typeNode.asText());
        if (type == null) {
            throw new JsonReadException(
                    context + ": unknown envelope binding type '" + typeNode.asText() + "'");
        }
        JsonNode value = node.get("value");
        switch (type) {
            case NULL:
                if (value != null && !value.isNull()) {
                    throw new JsonReadException(
                            context + ": null binding must not carry a value");
                }
                return BindingValue.nullValue();
            case INT32: {
                long parsed = parseInteger(value, context, "int32");
                if (parsed < Integer.MIN_VALUE || parsed > Integer.MAX_VALUE) {
                    throw new JsonReadException(
                            context + ": int32 value out of range: " + parsed);
                }
                return BindingValue.int32((int) parsed);
            }
            case INT64:
                return BindingValue.int64(parseInteger(value, context, "int64"));
            case BOOL:
                if (value == null || !value.isBoolean()) {
                    throw new JsonReadException(context + ": bool value must be true or false");
                }
                return BindingValue.bool(value.asBoolean());
            case TEXT:
                if (value == null || !value.isTextual()) {
                    throw new JsonReadException(context + ": text value must be a string");
                }
                return BindingValue.text(value.asText());
            case BLOB:
                if (value == null || !value.isTextual()) {
                    throw new JsonReadException(
                            context + ": blob value must be a base64 string");
                }
                if (!BASE64.matcher(value.asText()).matches()) {
                    throw new JsonReadException(context + ": blob value is not valid base64"
                            + " (standard alphabet, padded, no whitespace)");
                }
                return BindingValue.blob(Base64.getDecoder().decode(value.asText()));
            case FLOAT32: {
                double parsed = parseFloat(value, context, "float32");
                float single = (float) parsed;
                if (!Float.isFinite(single)) {
                    throw new JsonReadException(context
                            + ": float32 value overflows an IEEE-754 single: " + value);
                }
                return BindingValue.float32(single);
            }
            case FLOAT64:
                return BindingValue.float64(parseFloat(value, context, "float64"));
            default:
                throw new JsonReadException(context + ": unhandled binding type " + type);
        }
    }

    /**
     * int32/int64 wire rule: JSON number when |v| &le; 2^53−1 (writers
     * must use the string form beyond that), or a strict decimal string
     * (no whitespace, no '+').
     */
    private static long parseInteger(JsonNode value, String context, String typeName)
            throws JsonReadException {
        if (value == null || value.isNull()) {
            throw new JsonReadException(context + ": " + typeName + " value is missing");
        }
        if (value.isNumber()) {
            if (!value.isIntegralNumber()) {
                throw new JsonReadException(
                        context + ": " + typeName + " value must be integral: " + value);
            }
            BigInteger big = value.bigIntegerValue();
            if (big.abs().compareTo(MAX_SAFE_JSON_INTEGER) > 0) {
                throw new JsonReadException(context + ": " + typeName
                        + " number value exceeds 2^53-1, use the decimal string form: " + value);
            }
            return big.longValueExact();
        }
        if (value.isTextual()) {
            if (!DECIMAL_STRING.matcher(value.asText()).matches()) {
                throw new JsonReadException(context + ": " + typeName
                        + " value is not a decimal string: '" + value.asText() + "'");
            }
            try {
                return Long.parseLong(value.asText());
            } catch (NumberFormatException e) {
                throw new JsonReadException(context + ": " + typeName
                        + " value out of range: '" + value.asText() + "'");
            }
        }
        throw new JsonReadException(context + ": " + typeName
                + " value must be a number or decimal string");
    }

    /**
     * float32/float64 wire rule: a finite JSON number only (integral
     * numbers are valid float values); unlike int64, the string form is
     * never accepted because every IEEE-754 double round-trips through
     * a JSON number (docs/script-envelope.md).
     */
    private static double parseFloat(JsonNode value, String context, String typeName)
            throws JsonReadException {
        if (value == null || value.isNull()) {
            throw new JsonReadException(context + ": " + typeName + " value is missing");
        }
        if (!value.isNumber()) {
            throw new JsonReadException(context + ": " + typeName
                    + " value must be a JSON number (string form is not accepted)");
        }
        double parsed = value.doubleValue();
        if (!Double.isFinite(parsed)) {
            throw new JsonReadException(
                    context + ": " + typeName + " value must be finite: " + value);
        }
        return parsed;
    }

    private static String optionalString(JsonNode parent, String field)
            throws JsonReadException {
        JsonNode node = parent.get(field);
        if (node == null || node.isNull()) {
            return null;
        }
        if (!node.isTextual()) {
            throw new JsonReadException(field + " must be a string");
        }
        return node.asText();
    }

    private static Integer optionalInt(JsonNode parent, String field)
            throws JsonReadException {
        JsonNode node = parent.get(field);
        if (node == null || node.isNull()) {
            return null;
        }
        if (!node.isIntegralNumber() || !node.canConvertToInt()) {
            throw new JsonReadException(field + " must be an integer");
        }
        return node.asInt();
    }

    private static List<String> stringList(JsonNode parent, String field)
            throws JsonReadException {
        List<String> values = new ArrayList<>();
        JsonNode node = parent.get(field);
        if (node == null || node.isNull()) {
            return values;
        }
        if (!node.isArray()) {
            throw new JsonReadException(field + " must be an array of strings");
        }
        for (JsonNode entry : node) {
            if (!entry.isTextual()) {
                throw new JsonReadException(field + " must contain only strings");
            }
            values.add(entry.asText());
        }
        return values;
    }
}
