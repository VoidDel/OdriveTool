using OdriveUpper.Core.Firmware;
using YamlDotNet.RepresentationModel;

namespace OdriveUpper.Drivers.Firmware;

public sealed class OdriveInterfaceYamlSchemaProvider
{
    private const string DefaultInterfacePath = @"D:\xiafeng\Project\Odrive\ODrive\Firmware\odrive-interface.yaml";
    private readonly string _interfacePath;

    public OdriveInterfaceYamlSchemaProvider(string? interfacePath = null)
    {
        _interfacePath = interfacePath ?? DefaultInterfacePath;
    }

    public bool IsAvailable => File.Exists(_interfacePath);

    public DeviceApiSchema? TryLoad()
    {
        if (!IsAvailable)
        {
            return null;
        }

        using var reader = File.OpenText(_interfacePath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            return null;
        }

        var interfaces = GetMapping(root, "interfaces");
        if (interfaces is null)
        {
            return null;
        }

        var properties = new List<ApiProperty>();
        var commands = new List<ApiCommand>();
        ExpandInterface(HasKey(interfaces, "ODrive3") ? "ODrive3" : "ODrive", string.Empty, interfaces, properties, commands, []);

        AddLogicalTamagawaAliases(properties);

        return new DeviceApiSchema(
            SchemaId: "odrive-interface-yaml",
            DisplayName: "ODrive Firmware Interface YAML",
            Version: 1,
            Properties: properties
                .DistinctBy(property => property.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(property => property.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Commands: commands
                .DistinctBy(command => command.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(command => command.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void ExpandInterface(
        string interfaceName,
        string prefix,
        YamlMappingNode interfaces,
        List<ApiProperty> properties,
        List<ApiCommand> commands,
        HashSet<string> stack)
    {
        var iface = GetMapping(interfaces, interfaceName);
        if (iface is null)
        {
            return;
        }

        var stackKey = string.IsNullOrEmpty(prefix) ? interfaceName : $"{interfaceName}@{prefix}";
        if (!stack.Add(stackKey))
        {
            return;
        }

        if (ExtractString(iface, "implements") is { } implementsInterface)
        {
            ExpandInterface(implementsInterface, prefix, interfaces, properties, commands, stack);
        }

        if (GetMapping(iface, "attributes") is { } attributes)
        {
            ExpandAttributes(attributes, prefix, interfaces, properties, commands, stack);
        }

        if (GetMapping(iface, "functions") is { } functions)
        {
            ExpandFunctions(functions, prefix, commands);
        }

        stack.Remove(stackKey);
    }

    private static void ExpandAttributes(
        YamlMappingNode attributes,
        string prefix,
        YamlMappingNode interfaces,
        List<ApiProperty> properties,
        List<ApiCommand> commands,
        HashSet<string> stack)
    {
        foreach (var (keyNode, valueNode) in attributes.Children)
        {
            var name = ScalarValue(keyNode);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var path = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
            var typeName = ExtractType(valueNode);
            var nestedAttributes = valueNode is YamlMappingNode map ? GetMapping(map, "attributes") : null;

            if (nestedAttributes is not null)
            {
                if (valueNode is YamlMappingNode mapWithImplements && ExtractString(mapWithImplements, "implements") is { } implementsInterface)
                {
                    ExpandInterface(implementsInterface, path, interfaces, properties, commands, stack);
                }

                ExpandAttributes(nestedAttributes, path, interfaces, properties, commands, stack);
                continue;
            }

            var referencedInterface = ResolveInterfaceName(typeName, interfaces);
            if (referencedInterface is not null)
            {
                ExpandInterface(referencedInterface, path, interfaces, properties, commands, stack);
                continue;
            }

            properties.Add(CreateProperty(path, name, typeName, valueNode));
        }
    }

    private static void ExpandFunctions(YamlMappingNode functions, string prefix, List<ApiCommand> commands)
    {
        foreach (var (keyNode, valueNode) in functions.Children)
        {
            var name = ScalarValue(keyNode);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var path = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
            var arguments = new List<ApiCommandArgument>();
            if (valueNode is YamlMappingNode functionMap && GetMapping(functionMap, "in") is { } inputs)
            {
                foreach (var (argNameNode, argValueNode) in inputs.Children)
                {
                    var argName = ScalarValue(argNameNode);
                    var argType = ExtractType(argValueNode);
                    if (!string.IsNullOrWhiteSpace(argName))
                    {
                        arguments.Add(new ApiCommandArgument(argName, NormalizeType(argType)));
                    }
                }
            }

            commands.Add(new ApiCommand(path, path, arguments, ExtractString(valueNode, "doc")));
        }
    }

    private static ApiProperty CreateProperty(string path, string name, string? typeName, YamlNode node)
    {
        var normalized = NormalizeType(typeName);
        var isReadonly = normalized.StartsWith("readonly ", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith(".error", StringComparison.OrdinalIgnoreCase) ||
                         path == "error";
        normalized = normalized.Replace("readonly ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(normalized) && node is YamlMappingNode map && GetMapping(map, "flags") is not null)
        {
            normalized = "uint32";
            isReadonly = true;
        }

        return new ApiProperty(
            Path: path,
            DisplayName: path,
            ValueType: string.IsNullOrWhiteSpace(normalized) ? "object" : normalized,
            IsWritable: !isReadonly,
            Unit: node is YamlMappingNode ? ExtractString(node, "unit") : null,
            Description: node is YamlMappingNode ? ExtractString(node, "doc") ?? ExtractString(node, "brief") : null);
    }

    private static void AddLogicalTamagawaAliases(List<ApiProperty> properties)
    {
        if (properties.Any(property => property.Path.Equals("axis0.encoder.pos_abs", StringComparison.OrdinalIgnoreCase)))
        {
            properties.Add(new ApiProperty("axis0.tamagawa.absolute_position", "axis0.tamagawa.absolute_position", "uint32", false));
            properties.Add(new ApiProperty("axis0.tamagawa.multi_turn_count", "axis0.tamagawa.multi_turn_count", "int32", false));
            properties.Add(new ApiProperty("axis0.tamagawa.crc_error_count", "axis0.tamagawa.crc_error_count", "float32", false));
            properties.Add(new ApiProperty("axis0.tamagawa.warning_status", "axis0.tamagawa.warning_status", "uint32", false));
            properties.Add(new ApiProperty("axis0.tamagawa.error_status", "axis0.tamagawa.error_status", "uint32", false));
        }
    }

    private static string? ResolveInterfaceName(string? typeName, YamlMappingNode interfaces)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var clean = NormalizeType(typeName).Replace("readonly ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (HasKey(interfaces, clean))
        {
            return clean;
        }

        var qualified = clean.StartsWith("ODrive.", StringComparison.Ordinal) ? clean : $"ODrive.{clean}";
        return HasKey(interfaces, qualified) ? qualified : null;
    }

    private static string? ExtractType(YamlNode node)
    {
        if (node is YamlScalarNode scalar)
        {
            return scalar.Value;
        }

        if (node is YamlMappingNode map)
        {
            if (TryGetNode(map, "type", out var typeNode))
            {
                return ScalarValue(typeNode);
            }

            if (GetMapping(map, "flags") is not null)
            {
                return "readonly uint32";
            }
        }

        return null;
    }

    private static string NormalizeType(string? typeName) => (typeName ?? string.Empty).Trim();

    private static string? ExtractString(YamlNode node, string key)
    {
        return node is YamlMappingNode map && TryGetNode(map, key, out var value) ? ScalarValue(value) : null;
    }

    private static YamlMappingNode? GetMapping(YamlMappingNode node, string key)
    {
        return TryGetNode(node, key, out var value) ? value as YamlMappingNode : null;
    }

    private static bool HasKey(YamlMappingNode node, string key) => TryGetNode(node, key, out _);

    private static bool TryGetNode(YamlMappingNode node, string key, out YamlNode value)
    {
        foreach (var (keyNode, childValue) in node.Children)
        {
            if (string.Equals(ScalarValue(keyNode), key, StringComparison.Ordinal))
            {
                value = childValue;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static string? ScalarValue(YamlNode node) => node switch
    {
        YamlScalarNode scalar => scalar.Value,
        _ => null
    };
}
