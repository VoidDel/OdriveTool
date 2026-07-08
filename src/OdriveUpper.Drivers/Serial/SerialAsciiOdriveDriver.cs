using System.IO.Ports;
using System.Management;
using OdriveUpper.Core.Devices;

namespace OdriveUpper.Drivers.Serial;

public sealed class SerialAsciiOdriveDriver : IDeviceDriver
{
    private const int DefaultBaudRate = 115200;

    public string Name => "ODrive ASCII Serial Driver";

    public async Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var devices = new List<DeviceInfo>();
        var ports = GetCandidatePortNames();

        foreach (var portName in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var device = await TryCreateDeviceAsync(portName, cancellationToken);
            if (device is not null)
            {
                devices.Add(device);
            }
        }

        return devices;
    }

    public Task<IDeviceSession> ConnectAsync(string serialNumber, CancellationToken cancellationToken)
    {
        var portName = serialNumber.Split(':', 2)[0];
        return Task.FromResult<IDeviceSession>(new SerialAsciiOdriveSession(portName, DefaultBaudRate));
    }

    private static async Task<DeviceInfo?> TryCreateDeviceAsync(string portName, CancellationToken cancellationToken)
    {
        try
        {
            await using var session = new SerialAsciiOdriveSession(portName, DefaultBaudRate);
            var serial = await session.ReadScalarAsync("serial_number", cancellationToken);
            var fwMajor = await session.ReadScalarAsync("fw_version_major", cancellationToken);
            var fwMinor = await session.ReadScalarAsync("fw_version_minor", cancellationToken);
            var fwRevision = await session.ReadScalarAsync("fw_version_revision", cancellationToken);
            var hwMajor = await session.ReadScalarAsync("hw_version_major", cancellationToken);
            var hwMinor = await session.ReadScalarAsync("hw_version_minor", cancellationToken);
            var hwVariant = await session.ReadScalarAsync("hw_version_variant", cancellationToken);

            if (string.IsNullOrWhiteSpace(serial) || serial.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new DeviceInfo(
                SerialNumber: $"{portName}:{serial}",
                DisplayName: $"ODrive {serial} ({portName})",
                FirmwareVersion: $"{fwMajor}.{fwMinor}.{fwRevision}",
                HardwareVersion: $"{hwMajor}.{hwMinor}.{hwVariant}",
                DriverName: "ODrive ASCII Serial Driver",
                IsMock: false);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetCandidatePortNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var portName in GetWindowsOdrivePorts())
        {
            if (seen.Add(portName))
            {
                ordered.Add(portName);
            }
        }

        foreach (var portName in SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(portName))
            {
                ordered.Add(portName);
            }
        }

        return ordered;
    }

    private static IEnumerable<string> GetWindowsOdrivePorts()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%' AND PNPDeviceID LIKE '%VID_1209&PID_0D32%'");

        foreach (var item in searcher.Get().OfType<ManagementObject>())
        {
            var name = Convert.ToString(item["Name"]);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var start = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
            var end = name.LastIndexOf(')');
            if (start < 0 || end <= start)
            {
                continue;
            }

            yield return name[(start + 1)..end];
        }
    }
}
