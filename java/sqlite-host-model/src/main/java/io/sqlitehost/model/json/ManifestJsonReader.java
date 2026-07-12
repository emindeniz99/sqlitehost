package io.sqlitehost.model.json;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.sqlitehost.model.manifest.InlineArg;
import io.sqlitehost.model.manifest.InlineFunction;
import io.sqlitehost.model.manifest.InlineReturn;
import io.sqlitehost.model.manifest.ListField;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.model.manifest.ManifestColumns;
import io.sqlitehost.model.manifest.ManifestLibrary;
import io.sqlitehost.model.manifest.ManifestNaming;
import io.sqlitehost.model.manifest.ManifestTable;
import io.sqlitehost.model.manifest.MethodDescriptor;
import io.sqlitehost.model.manifest.ObjectShape;
import io.sqlitehost.model.manifest.ScalarField;
import io.sqlitehost.model.manifest.ScalarType;
import io.sqlitehost.model.manifest.ScriptEnvelopeDescriptor;

import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;

/**
 * Strict JSON reader for the canonical manifest (docs/manifest.md).
 * The manifest is trusted generated output, but shape violations
 * (missing blocks, unknown scalar types) fail loudly rather than
 * producing a half-built model.
 */
public final class ManifestJsonReader {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    private ManifestJsonReader() {
    }

    public static Manifest read(String json) throws IOException {
        return fromTree(MAPPER.readTree(json));
    }

    public static Manifest read(InputStream in) throws IOException {
        return fromTree(MAPPER.readTree(in));
    }

    private static Manifest fromTree(JsonNode root) throws JsonReadException {
        if (root == null || !root.isObject()) {
            throw new JsonReadException("manifest must be a JSON object");
        }
        JsonNode libraryNode = requireObject(root, "library");
        ManifestLibrary library = new ManifestLibrary(
                requireString(libraryNode, "namespace"),
                requireString(libraryNode, "interfaceName"),
                requireInt(libraryNode, "apiLevel"),
                requireInt(libraryNode, "minSqliteVersionNumber"),
                requireStringList(libraryNode, "features"));

        JsonNode namingNode = requireObject(root, "naming");
        ManifestNaming naming = new ManifestNaming(
                requireString(namingNode, "callTablePrefix"),
                requireString(namingNode, "resultTablePrefix"),
                requireString(namingNode, "inputColumnPrefix"),
                requireString(namingNode, "resultColumnPrefix"),
                requireString(namingNode, "inputListTableInfix"),
                requireString(namingNode, "resultListTableInfix"),
                requireString(namingNode, "functionPrefix"));

        JsonNode columnsNode = requireObject(root, "columns");
        ManifestColumns columns = new ManifestColumns(
                requireString(columnsNode, "callId"),
                requireString(columnsNode, "itemIndex"),
                requireString(columnsNode, "status"),
                requireString(columnsNode, "doneValue"),
                requireString(columnsNode, "queueId"),
                requireString(columnsNode, "method"),
                requireString(columnsNode, "name"),
                requireString(columnsNode, "valueType"),
                requireString(columnsNode, "intValue"),
                requireString(columnsNode, "realValue"),
                requireString(columnsNode, "textValue"),
                requireString(columnsNode, "blobValue"),
                requireString(columnsNode, "action"),
                requireString(columnsNode, "message"));

        JsonNode envelopeNode = requireObject(root, "scriptEnvelope");
        ScriptEnvelopeDescriptor scriptEnvelope = new ScriptEnvelopeDescriptor(
                requireString(envelopeNode, "engine"),
                requireStringList(envelopeNode, "bindingTypes"));

        List<MethodDescriptor> methods = new ArrayList<>();
        JsonNode methodsNode = root.get("methods");
        if (methodsNode == null || !methodsNode.isArray()) {
            throw new JsonReadException("manifest methods must be an array");
        }
        for (JsonNode methodNode : methodsNode) {
            methods.add(readMethod(methodNode));
        }

        return new Manifest(
                requireInt(root, "manifestVersion"),
                requireString(root, "engine"),
                library,
                naming,
                columns,
                readTable(requireObject(root, "queueTable")),
                readTable(requireObject(root, "inputsTable")),
                readTable(requireObject(root, "varsTable")),
                readTable(requireObject(root, "controlTable")),
                scriptEnvelope,
                methods);
    }

    private static ManifestTable readTable(JsonNode node) throws JsonReadException {
        return new ManifestTable(
                requireString(node, "name"),
                requireStringList(node, "columns"));
    }

    private static MethodDescriptor readMethod(JsonNode node) throws JsonReadException {
        if (node == null || !node.isObject()) {
            throw new JsonReadException("manifest method must be an object");
        }
        return new MethodDescriptor(
                requireString(node, "operationName"),
                requireString(node, "methodName"),
                requireString(node, "handlerName"),
                requireInt(node, "apiLevel"),
                requireBoolean(node, "mutates"),
                requireString(node, "callTable"),
                requireString(node, "resultTable"),
                requireString(node, "queueTrigger"),
                readShape(requireObject(node, "input")),
                readShape(requireObject(node, "result")),
                readInline(node));
    }

    /**
     * The {@code inline} block is required but nullable: {@code null}
     * when the method is not inline-exposed, an object otherwise
     * (docs/manifest.md).
     */
    private static InlineFunction readInline(JsonNode methodNode) throws JsonReadException {
        JsonNode node = methodNode.get("inline");
        if (node == null) {
            throw new JsonReadException(
                    "manifest field 'inline' must be present (null when not exposed)");
        }
        if (node.isNull()) {
            return null;
        }
        if (!node.isObject()) {
            throw new JsonReadException("manifest field 'inline' must be an object or null");
        }
        List<InlineArg> args = new ArrayList<>();
        JsonNode argsNode = node.get("args");
        if (argsNode == null || !argsNode.isArray()) {
            throw new JsonReadException("inline args must be an array");
        }
        for (JsonNode argNode : argsNode) {
            if (argNode == null || !argNode.isObject()) {
                throw new JsonReadException("inline arg must be an object");
            }
            args.add(new InlineArg(
                    requireString(argNode, "propertyName"),
                    requireString(argNode, "sqlName"),
                    requireScalarType(argNode),
                    requireBoolean(argNode, "optional")));
        }
        JsonNode returnsNode = requireObject(node, "returns");
        InlineReturn returns = new InlineReturn(
                requireString(returnsNode, "propertyName"),
                requireString(returnsNode, "sqlName"),
                requireScalarType(returnsNode));
        return new InlineFunction(
                requireString(node, "functionName"),
                requireInt(node, "minArgs"),
                requireInt(node, "maxArgs"),
                args,
                returns);
    }

    private static ObjectShape readShape(JsonNode node) throws JsonReadException {
        List<ScalarField> fields = new ArrayList<>();
        JsonNode fieldsNode = node.get("fields");
        if (fieldsNode == null || !fieldsNode.isArray()) {
            throw new JsonReadException("shape fields must be an array");
        }
        for (JsonNode fieldNode : fieldsNode) {
            fields.add(readScalarField(fieldNode));
        }
        List<ListField> listFields = new ArrayList<>();
        JsonNode listFieldsNode = node.get("listFields");
        if (listFieldsNode == null || !listFieldsNode.isArray()) {
            throw new JsonReadException("shape listFields must be an array");
        }
        for (JsonNode listFieldNode : listFieldsNode) {
            List<ScalarField> itemFields = new ArrayList<>();
            JsonNode itemFieldsNode = listFieldNode.get("itemFields");
            if (itemFieldsNode == null || !itemFieldsNode.isArray()) {
                throw new JsonReadException("list field itemFields must be an array");
            }
            for (JsonNode itemFieldNode : itemFieldsNode) {
                itemFields.add(readScalarField(itemFieldNode));
            }
            listFields.add(new ListField(
                    requireString(listFieldNode, "propertyName"),
                    requireString(listFieldNode, "sqlName"),
                    requireString(listFieldNode, "childTable"),
                    requireString(listFieldNode, "itemModelName"),
                    itemFields));
        }
        return new ObjectShape(requireString(node, "modelName"), fields, listFields);
    }

    private static ScalarField readScalarField(JsonNode node) throws JsonReadException {
        if (node == null || !node.isObject()) {
            throw new JsonReadException("scalar field must be an object");
        }
        JsonNode optionalNode = node.get("optional");
        if (optionalNode == null || !optionalNode.isBoolean()) {
            throw new JsonReadException("scalar field optional must be a boolean");
        }
        return new ScalarField(
                requireString(node, "propertyName"),
                requireString(node, "sqlName"),
                requireString(node, "column"),
                requireScalarType(node),
                optionalNode.asBoolean());
    }

    private static ScalarType requireScalarType(JsonNode node) throws JsonReadException {
        String scalarTypeName = requireString(node, "scalarType");
        ScalarType scalarType = ScalarType.fromJsonName(scalarTypeName);
        if (scalarType == null) {
            throw new JsonReadException("unknown scalar type '" + scalarTypeName + "'");
        }
        return scalarType;
    }

    private static JsonNode requireObject(JsonNode parent, String field)
            throws JsonReadException {
        JsonNode node = parent.get(field);
        if (node == null || !node.isObject()) {
            throw new JsonReadException("manifest field '" + field + "' must be an object");
        }
        return node;
    }

    private static String requireString(JsonNode parent, String field)
            throws JsonReadException {
        JsonNode node = parent.get(field);
        if (node == null || !node.isTextual()) {
            throw new JsonReadException("manifest field '" + field + "' must be a string");
        }
        return node.asText();
    }

    private static boolean requireBoolean(JsonNode parent, String field)
            throws JsonReadException {
        JsonNode node = parent.get(field);
        if (node == null || !node.isBoolean()) {
            throw new JsonReadException("manifest field '" + field + "' must be a boolean");
        }
        return node.asBoolean();
    }

    private static int requireInt(JsonNode parent, String field) throws JsonReadException {
        JsonNode node = parent.get(field);
        if (node == null || !node.isIntegralNumber() || !node.canConvertToInt()) {
            throw new JsonReadException("manifest field '" + field + "' must be an integer");
        }
        return node.asInt();
    }

    private static List<String> requireStringList(JsonNode parent, String field)
            throws JsonReadException {
        JsonNode node = parent.get(field);
        if (node == null || !node.isArray()) {
            throw new JsonReadException(
                    "manifest field '" + field + "' must be an array of strings");
        }
        List<String> values = new ArrayList<>();
        for (JsonNode entry : node) {
            if (!entry.isTextual()) {
                throw new JsonReadException(
                        "manifest field '" + field + "' must contain only strings");
            }
            values.add(entry.asText());
        }
        return values;
    }
}
