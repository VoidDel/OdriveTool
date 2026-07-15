using System.Globalization;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using OdriveUpper.Core.Devices;
using OdriveUpper.Core.Firmware;
using OdriveUpper.Drivers.Firmware;
using OdriveUpper.Core.Telemetry;

namespace OdriveUpper.Drivers.Serial;

public sealed class SerialAsciiOdriveSession : IDeviceSession
{
    private static readonly string[] DefaultSchemaPaths =
    [
        "vbus_voltage",
        "axis0.error",
        "axis0.current_state",
        "axis0.encoder.pos_estimate",
        "axis0.encoder.vel_estimate",
        "ibus",
        "axis0.motor.I_bus",
        "axis1.motor.I_bus",
        "axis0.controller.config.pos_gain",
        "axis0.controller.config.vel_gain",
        "axis0.controller.config.vel_integrator_gain",
        "axis0.controller.input_pos",
        "axis0.controller.input_vel",
        "axis0.controller.input_torque",
        "axis0.requested_state",
        "axis0.encoder.config.mode",
        "axis0.encoder.pos_abs",
        "axis0.encoder.pos_circular",
        "axis0.encoder.count_in_cpr",
        "axis0.encoder.shadow_count",
        "axis0.encoder.error",
        "axis0.encoder.spi_error_rate",
        "axis0.encoder.is_ready",
        "axis0.tamagawa.absolute_position",
        "axis0.tamagawa.multi_turn_count",
        "axis0.tamagawa.crc_error_count",
        "axis0.tamagawa.warning_status",
        "axis0.tamagawa.error_status"
    ];

    private readonly SerialPort _port;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly DeviceInfo _info;
    private int _disposed;

    public SerialAsciiOdriveSession(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            NewLine = "\n",
            ReadTimeout = 700,
            WriteTimeout = 700
        };
        _port.Open();
        _port.DiscardInBuffer();

        var serial = ReadScalarCore("serial_number");
        var fwMajor = ReadScalarCore("fw_version_major");
        var fwMinor = ReadScalarCore("fw_version_minor");
        var fwRevision = ReadScalarCore("fw_version_revision");
        var hwMajor = ReadScalarCore("hw_version_major");
        var hwMinor = ReadScalarCore("hw_version_minor");
        var hwVariant = ReadScalarCore("hw_version_variant");

        _info = new DeviceInfo(
            SerialNumber: $"{portName}:{serial}",
            DisplayName: $"ODrive {serial}",
            FirmwareVersion: $"{fwMajor}.{fwMinor}.{fwRevision}",
            HardwareVersion: $"{hwMajor}.{hwMinor}.{hwVariant}",
            DriverName: "ODrive ASCII Serial Driver",
            IsMock: false);
    }

    public DeviceInfo Info => _info;

    public async Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var encoderMode = await ReadScalarAsync("axis0.encoder.config.mode", cancellationToken);
        var hasTamagawaMode = long.TryParse(encoderMode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode) && mode == 0x105;
        var hasTamagawaNamespace = !IsInvalidProperty(await ReadScalarAsync("axis0.tamagawa.absolute_position", cancellationToken));
        var supportsTamagawa = hasTamagawaMode || hasTamagawaNamespace;

        return new DeviceCapabilities(
            SupportsOfficialOdriveCompatibility: true,
            SupportsCustomFirmwareExtensions: supportsTamagawa,
            SupportsTamagawaEncoder: supportsTamagawa,
            SupportsLiveTelemetry: true,
            SupportsConfigurationSave: true,
            EncoderTypes: supportsTamagawa ? ["Incremental", "Hall", "Tamagawa"] : ["Incremental", "Hall"],
            CustomFeatureFlags: supportsTamagawa
                ? ["ascii-serial", hasTamagawaNamespace ? "tamagawa-namespace" : "tamagawa-mode-0x105", "encoder-pos-abs"]
                : ["ascii-serial", "official-0.5.x-compatible"]);
    }

    public async Task<DeviceApiSchema> GetApiSchemaAsync(CancellationToken cancellationToken)
    {
        var provider = new OdriveInterfaceYamlSchemaProvider();
        var yamlSchema = provider.TryLoad();
        if (yamlSchema is not null)
        {
            return yamlSchema;
        }

        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        var properties = DefaultSchemaPaths
            .Where(path => capabilities.SupportsTamagawaEncoder || !path.Contains(".tamagawa.", StringComparison.OrdinalIgnoreCase))
            .Select(path => new ApiProperty(path, path, GuessType(path), IsWritable(path), GuessUnit(path)))
            .ToArray();

        var commands = new List<ApiCommand>
        {
            new("clear_errors", "Clear Errors", []),
            new("save_configuration", "Save Configuration", []),
            new("reboot", "Reboot", [])
        };

        if (capabilities.SupportsTamagawaEncoder)
        {
            commands.Add(new ApiCommand("axis0.tamagawa.run_self_test", "Run Tamagawa Self Test", []));
            commands.Add(new ApiCommand("axis0.tamagawa.clear_alarm", "Clear Tamagawa Alarm", []));
            commands.Add(new ApiCommand("axis0.tamagawa.calibrate_zero", "Calibrate Tamagawa Zero", []));
        }

        return new DeviceApiSchema(
            SchemaId: capabilities.SupportsTamagawaEncoder ? "custom-odrive-ascii-tamagawa" : "official-odrive-ascii",
            DisplayName: capabilities.SupportsTamagawaEncoder ? "ODrive ASCII + Tamagawa Extensions" : "ODrive ASCII Official Compatibility",
            Version: 1,
            Properties: properties,
            Commands: commands);
    }

    public async Task<PropertyReadResult> ReadAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var read = await ReadMappedValueAsync(path, cancellationToken);
            if (read.Success)
            {
                values[path] = read.Value;
            }
            else
            {
                errors[path] = read.Error ?? "read failed";
            }
        }

        return new PropertyReadResult(values, errors);
    }

    public async Task<IReadOnlyList<PropertyWriteResult>> WriteAsync(IReadOnlyList<PropertyWrite> writes, CancellationToken cancellationToken)
    {
        var results = new List<PropertyWriteResult>(writes.Count);

        foreach (var write in writes)
        {
            var actualPath = MapWritePath(write.Path);
            var value = Convert.ToString(write.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            var response = await ExecuteAsciiAsync($"w {actualPath} {value}", cancellationToken);
            results.Add(IsErrorResponse(response)
                ? new PropertyWriteResult(write.Path, false, response)
                : new PropertyWriteResult(write.Path, true));
        }

        return results;
    }

    public async Task<CommandResult> InvokeAsync(string path, IReadOnlyList<object?> args, CancellationToken cancellationToken)
    {
        var command = path switch
        {
            "save_configuration" => "ss",
            "clear_errors" => "sc",
            "reboot" => "sr",
            _ when path.StartsWith("axis0.tamagawa.", StringComparison.OrdinalIgnoreCase) => null,
            _ => null
        };

        if (command is null)
        {
            return new CommandResult(false, Error: $"ASCII serial driver cannot invoke '{path}'. Expose it through ASCII or use a Fibre/native driver.");
        }

        var response = await ExecuteAsciiAsync(command, cancellationToken);
        return IsErrorResponse(response)
            ? new CommandResult(false, Error: response)
            : new CommandResult(true, string.IsNullOrWhiteSpace(response) ? "OK" : response);
    }

    public async IAsyncEnumerable<TelemetryFrame> SubscribeTelemetryAsync(
        TelemetrySubscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in subscription.Paths)
            {
                var read = await ReadMappedValueAsync(path, cancellationToken);
                if (read.Success)
                {
                    values[path] = read.Value;
                }
            }

            yield return new TelemetryFrame(DateTimeOffset.UtcNow, values);
            await Task.Delay(subscription.Interval, cancellationToken);
        }
    }

    public async Task<string> ReadScalarAsync(string path, CancellationToken cancellationToken)
    {
        return await ExecuteAsciiAsync($"r {path}", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _port.Close();
        }
        catch (InvalidOperationException)
        {
            // The port was already closed after an interrupted read.
        }

        await _ioLock.WaitAsync();
        try
        {
            _port.Dispose();
        }
        finally
        {
            _ioLock.Release();
            _ioLock.Dispose();
        }
    }

    private string ReadScalarCore(string path)
    {
        _port.Write($"r {path}\n");
        return _port.ReadLine().Trim();
    }

    private async Task<string> ExecuteAsciiAsync(string command, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            _port.DiscardInBuffer();
            _port.Write(command + "\n");
            return await Task.Run(() => _port.ReadLine().Trim(), cancellationToken);
        }
        catch (TimeoutException)
        {
            return string.Empty;
        }
        catch (IOException ex)
        {
            return $"error: serial connection unavailable ({ex.Message})";
        }
        catch (InvalidOperationException ex)
        {
            return $"error: serial connection unavailable ({ex.Message})";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"error: serial connection unavailable ({ex.Message})";
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<(bool Success, object? Value, string? Error)> ReadMappedValueAsync(string path, CancellationToken cancellationToken)
    {
        var (paths, converter) = MapReadPath(path);
        string? lastError = null;

        foreach (var candidate in paths)
        {
            var response = await ReadScalarAsync(candidate, cancellationToken);
            if (string.IsNullOrWhiteSpace(response) || IsInvalidProperty(response) || IsErrorResponse(response))
            {
                lastError = response;
                continue;
            }

            return (true, converter(ParseScalar(response)), null);
        }

        return (false, null, lastError ?? "invalid property");
    }

    private static (IReadOnlyList<string> Paths, Func<object, object> Convert) MapReadPath(string path) => path switch
    {
        "axis0.tamagawa.absolute_position" => (["axis0.encoder.pos_abs"], value => value),
        "axis0.tamagawa.multi_turn_count" => (["axis0.encoder.tamagawa_multi_turn", "axis0.encoder.pos_estimate"], value => value is double or float or decimal ? (long)Math.Floor(ToDouble(value)) : value),
        "axis0.tamagawa.crc_error_count" => (["axis0.encoder.tamagawa_error_count", "axis0.encoder.spi_error_rate"], value => value),
        "axis0.tamagawa.warning_status" => (["axis0.encoder.tamagawa_status", "axis0.encoder.error"], value => value),
        "axis0.tamagawa.error_status" => (["axis0.encoder.tamagawa_alarm_code", "axis0.encoder.error"], value => value),
        _ => ([path], value => value)
    };

    private static string MapWritePath(string path) => path switch
    {
        _ => path
    };

    private static double ToDouble(object value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        _ => 0.0
    };

    private static object ParseScalar(string response)
    {
        if (long.TryParse(response, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(response, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return response;
    }

    private static bool IsInvalidProperty(string response) => response.Contains("invalid property", StringComparison.OrdinalIgnoreCase);

    private static bool IsErrorResponse(string response) =>
        response.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
        response.Contains("error", StringComparison.OrdinalIgnoreCase);

    private static bool IsWritable(string path) =>
        path.Contains(".config.", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(".input_", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("requested_state", StringComparison.OrdinalIgnoreCase);

    private static string GuessType(string path) => path switch
    {
        var p when p.Contains("state", StringComparison.OrdinalIgnoreCase) => "enum",
        var p when p.Contains("error", StringComparison.OrdinalIgnoreCase) => "uint32",
        _ => "float"
    };

    private static string? GuessUnit(string path) => path switch
    {
        "vbus_voltage" => "V",
        var p when p.Contains("pos", StringComparison.OrdinalIgnoreCase) => "turn",
        var p when p.Contains("vel", StringComparison.OrdinalIgnoreCase) => "turn/s",
        var p when p.Contains("current", StringComparison.OrdinalIgnoreCase) => "A",
        _ => null
    };
}
