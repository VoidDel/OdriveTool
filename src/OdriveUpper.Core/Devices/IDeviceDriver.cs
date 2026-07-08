using OdriveUpper.Core.Firmware;
using OdriveUpper.Core.Telemetry;

namespace OdriveUpper.Core.Devices;

public interface IDeviceDriver
{
    string Name { get; }

    Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken);

    Task<IDeviceSession> ConnectAsync(string serialNumber, CancellationToken cancellationToken);
}

public interface IDeviceSession : IAsyncDisposable
{
    DeviceInfo Info { get; }

    Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);

    Task<DeviceApiSchema> GetApiSchemaAsync(CancellationToken cancellationToken);

    Task<PropertyReadResult> ReadAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);

    Task<IReadOnlyList<PropertyWriteResult>> WriteAsync(IReadOnlyList<PropertyWrite> writes, CancellationToken cancellationToken);

    Task<CommandResult> InvokeAsync(string path, IReadOnlyList<object?> args, CancellationToken cancellationToken);

    IAsyncEnumerable<TelemetryFrame> SubscribeTelemetryAsync(TelemetrySubscription subscription, CancellationToken cancellationToken);
}
