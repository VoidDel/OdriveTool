using System.Runtime.CompilerServices;
using OdriveUpper.Core.Devices;
using OdriveUpper.Core.Firmware;
using OdriveUpper.Core.Telemetry;

namespace OdriveUpper.Drivers.Mock;

public sealed class MockOdriveSession(DeviceInfo info) : IDeviceSession
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vbus_voltage"] = 24.0,
        ["axis0.encoder.type"] = "Tamagawa",
        ["axis0.encoder.pos_estimate"] = 0.0,
        ["axis0.encoder.vel_estimate"] = 0.0,
        ["axis0.tamagawa.encoder_id"] = 1,
        ["axis0.tamagawa.crc_error_count"] = 0,
        ["axis0.motor.current_control.iq_measured"] = 0.0,
        ["axis0.requested_state"] = "Idle"
    };

    public DeviceInfo Info { get; } = info;

    public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var capabilities = new DeviceCapabilities(
            SupportsOfficialOdriveCompatibility: false,
            SupportsCustomFirmwareExtensions: true,
            SupportsTamagawaEncoder: true,
            SupportsLiveTelemetry: true,
            SupportsConfigurationSave: true,
            EncoderTypes: ["Incremental", "Hall", "Tamagawa"],
            CustomFeatureFlags: ["custom-firmware", "tamagawa-encoder", "schema-driven-ui"]);

        return Task.FromResult(capabilities);
    }

    public Task<DeviceApiSchema> GetApiSchemaAsync(CancellationToken cancellationToken)
    {
        var schema = new DeviceApiSchema(
            SchemaId: "custom-odrive-v3.6-tamagawa-dev",
            DisplayName: "Custom ODrive v3.6 + Tamagawa Encoder",
            Version: 1,
            Properties:
            [
                new ApiProperty("vbus_voltage", "Bus Voltage", "float", false, "V"),
                new ApiProperty("axis0.encoder.type", "Axis 0 Encoder Type", "enum", true),
                new ApiProperty("axis0.encoder.pos_estimate", "Axis 0 Position Estimate", "float", false, "turn"),
                new ApiProperty("axis0.encoder.vel_estimate", "Axis 0 Velocity Estimate", "float", false, "turn/s"),
                new ApiProperty("axis0.tamagawa.encoder_id", "Axis 0 Tamagawa Encoder ID", "int", true),
                new ApiProperty("axis0.tamagawa.crc_error_count", "Axis 0 Tamagawa CRC Errors", "int", false),
                new ApiProperty("axis0.motor.current_control.iq_measured", "Axis 0 Iq Measured", "float", false, "A"),
                new ApiProperty("axis0.requested_state", "Axis 0 Requested State", "enum", true)
            ],
            Commands:
            [
                new ApiCommand("clear_errors", "Clear Errors", []),
                new ApiCommand("save_configuration", "Save Configuration", []),
                new ApiCommand("reboot", "Reboot", []),
                new ApiCommand("axis0.tamagawa.run_self_test", "Run Tamagawa Self Test", [])
            ]);

        return Task.FromResult(schema);
    }

    public Task<PropertyReadResult> ReadAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (_values.TryGetValue(path, out var value))
            {
                values[path] = value;
            }
            else
            {
                errors[path] = "Path not found in mock device schema.";
            }
        }

        return Task.FromResult(new PropertyReadResult(values, errors));
    }

    public Task<IReadOnlyList<PropertyWriteResult>> WriteAsync(IReadOnlyList<PropertyWrite> writes, CancellationToken cancellationToken)
    {
        var results = new List<PropertyWriteResult>(writes.Count);

        foreach (var write in writes)
        {
            if (!_values.ContainsKey(write.Path))
            {
                results.Add(new PropertyWriteResult(write.Path, false, "Path not found in mock device schema."));
                continue;
            }

            _values[write.Path] = write.Value;
            results.Add(new PropertyWriteResult(write.Path, true));
        }

        return Task.FromResult<IReadOnlyList<PropertyWriteResult>>(results);
    }

    public Task<CommandResult> InvokeAsync(string path, IReadOnlyList<object?> args, CancellationToken cancellationToken)
    {
        var result = path switch
        {
            "clear_errors" => new CommandResult(true, "Errors cleared."),
            "save_configuration" => new CommandResult(true, "Configuration saved."),
            "reboot" => new CommandResult(true, "Mock reboot requested."),
            "axis0.tamagawa.run_self_test" => new CommandResult(true, "Tamagawa self-test passed."),
            _ => new CommandResult(false, Error: $"Command '{path}' is not available on the mock device.")
        };

        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<TelemetryFrame> SubscribeTelemetryAsync(
        TelemetrySubscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var sample = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = DateTimeOffset.UtcNow - started;
            var position = Math.Sin(elapsed.TotalSeconds);
            var velocity = Math.Cos(elapsed.TotalSeconds);
            var iq = 1.5 * Math.Sin(elapsed.TotalSeconds * 0.5);

            _values["axis0.encoder.pos_estimate"] = Math.Round(position, 4);
            _values["axis0.encoder.vel_estimate"] = Math.Round(velocity, 4);
            _values["axis0.motor.current_control.iq_measured"] = Math.Round(iq, 4);
            _values["vbus_voltage"] = Math.Round(24.0 + 0.2 * Math.Sin(elapsed.TotalSeconds * 0.2), 3);
            _values["axis0.tamagawa.crc_error_count"] = sample / 200;

            var values = subscription.Paths
                .Where(_values.ContainsKey)
                .ToDictionary(path => path, path => _values[path], StringComparer.OrdinalIgnoreCase);

            yield return new TelemetryFrame(DateTimeOffset.UtcNow, values);

            sample++;
            await Task.Delay(subscription.Interval, cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
