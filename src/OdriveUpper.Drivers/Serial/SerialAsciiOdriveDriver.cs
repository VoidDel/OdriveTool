using System.Diagnostics;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
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

            if (!ulong.TryParse(serial, out _) ||
                !Version.TryParse(device.FirmwareVersion, out _) ||
                !Version.TryParse(device.HardwareVersion, out _))
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

        var odriveUsbSerials = GetMacOSOdriveUsbSerials();
        if (odriveUsbSerials.Count == 0)
        {
            yield break;
        }

        string[] portNames;
        try
        {
            portNames = Directory.EnumerateFiles("/dev", "cu.usb*").ToArray();
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
            .Where(port => odriveUsbSerials.Any(serial => port.Contains(serial, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return portName;
        }
    }

    private static IReadOnlySet<string> GetMacOSOdriveUsbSerials()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/sbin/ioreg",
                Arguments = "-r -c IOUSBHostDevice -l -w 0",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(2000))
            {
                process.Kill(entireProcessTree: true);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var output = outputTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return Regex.Matches(
                    output,
                    @"\+\-o[^\r\n]*\bODrive\b[^\r\n]*\r?\n.*?\""kUSBSerialNumberString\""\s*=\s*\""(?<serial>[^\""]+)\""",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(match => match.Groups["serial"].Value)
                .Where(serial => !string.IsNullOrWhiteSpace(serial))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
