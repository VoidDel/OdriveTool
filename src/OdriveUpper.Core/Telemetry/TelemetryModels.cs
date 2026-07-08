namespace OdriveUpper.Core.Telemetry;

public sealed record TelemetrySubscription(IReadOnlyList<string> Paths, TimeSpan Interval);

public sealed record TelemetryFrame(DateTimeOffset Timestamp, IReadOnlyDictionary<string, object?> Values);
