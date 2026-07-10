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

        foreach (var portName in GetCandidatePortNames())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Some macOS virtual serial ports never honor SerialPort.ReadTimeout.
            // Bound each probe so one such port cannot leave the UI scanning forever.
            var probeTask = Task.Factory.StartNew(
                () => TryCreateDeviceAsync(portName, cancellationToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
            DeviceInfo? device;
            try
            {
                device = await probeTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                continue;
            }

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
            cancellationToken.ThrowIfCancellationRequested();
            await using var session = new SerialAsciiOdriveSession(portName, DefaultBaudRate);
            var device = session.Info;
            var serial = device.SerialNumber.Split(':', 2).LastOrDefault();

            if (string.IsNullOrWhiteSpace(serial) || serial.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return device;
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
        var portSources = OperatingSystem.IsMacOS()
            ? GetMacOSSerialPorts()
            : GetWindowsOdrivePorts().Concat(SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase));

        foreach (var portName in portSources)
        {
            if (seen.Add(portName))
            {
                ordered.Add(portName);
            }
        }

        return ordered;
    }

    private static IEnumerable<string> GetMacOSSerialPorts()
    {
        if (!OperatingSystem.IsMacOS())
        {
            yield break;
        }

        string[] portNames;
        try
        {
            portNames =
            [
                .. Directory.EnumerateFiles("/dev", "cu.usb*"),
                .. Directory.EnumerateFiles("/dev", "cu.SLAB_USBtoUART*"),
                .. Directory.EnumerateFiles("/dev", "cu.wchusbserial*")
            ];
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var portName in portNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return portName;
        }
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
