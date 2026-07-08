namespace OdriveUpper.Core.Devices;

public sealed record DeviceCapabilities(
    bool SupportsOfficialOdriveCompatibility,
    bool SupportsCustomFirmwareExtensions,
    bool SupportsTamagawaEncoder,
    bool SupportsLiveTelemetry,
    bool SupportsConfigurationSave,
    IReadOnlyList<string> EncoderTypes,
    IReadOnlyList<string> CustomFeatureFlags);
