namespace OdriveUpper.Core.Firmware;

public sealed record DeviceApiSchema(
    string SchemaId,
    string DisplayName,
    int Version,
    IReadOnlyList<ApiProperty> Properties,
    IReadOnlyList<ApiCommand> Commands);

public sealed record ApiProperty(
    string Path,
    string DisplayName,
    string ValueType,
    bool IsWritable,
    string? Unit = null,
    string? Description = null);

public sealed record ApiCommand(
    string Path,
    string DisplayName,
    IReadOnlyList<ApiCommandArgument> Arguments,
    string? Description = null);

public sealed record ApiCommandArgument(string Name, string ValueType, bool IsRequired = true);
