namespace OdriveUpper.Core.Devices;

public sealed record DeviceInfo(
    string SerialNumber,
    string DisplayName,
    string FirmwareVersion,
    string HardwareVersion,
    string DriverName,
    bool IsMock);
