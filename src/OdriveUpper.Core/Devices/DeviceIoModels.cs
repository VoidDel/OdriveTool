namespace OdriveUpper.Core.Devices;

public sealed record PropertyWrite(string Path, object? Value);

public sealed record PropertyWriteResult(string Path, bool Success, string? Error = null);

public sealed record PropertyReadResult(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyDictionary<string, string> Errors);

public sealed record CommandResult(bool Success, object? Result = null, string? Error = null);
