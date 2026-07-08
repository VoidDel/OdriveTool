using OdriveUpper.Core.Devices;

namespace OdriveUpper.Drivers.Mock;

public sealed class MockOdriveDriver : IDeviceDriver
{
    private static readonly DeviceInfo MockDevice = new(
        SerialNumber: "MOCK-TAMAGAWA-0001",
        DisplayName: "Custom ODrive Mock - Tamagawa Encoder",
        FirmwareVersion: "custom-0.1.0-dev",
        HardwareVersion: "ODrive v3.6 custom",
        DriverName: nameof(MockOdriveDriver),
        IsMock: true);

    public string Name => "Mock ODrive Driver";

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DeviceInfo> devices = [MockDevice];
        return Task.FromResult(devices);
    }

    public Task<IDeviceSession> ConnectAsync(string serialNumber, CancellationToken cancellationToken)
    {
        if (!string.Equals(serialNumber, MockDevice.SerialNumber, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Mock device '{serialNumber}' was not found.");
        }

        return Task.FromResult<IDeviceSession>(new MockOdriveSession(MockDevice));
    }
}
