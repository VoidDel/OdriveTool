using OdriveUpper.Core.Devices;

namespace OdriveUpper.App.ViewModels;

public sealed class DeviceListItemViewModel(DeviceInfo info, IDeviceDriver driver)
{
    public DeviceInfo Info { get; } = info;

    public IDeviceDriver Driver { get; } = driver;

    public string SerialNumber => Info.SerialNumber;

    public string DisplayName => Info.DisplayName;

    public string FirmwareVersion => Info.FirmwareVersion;

    public string DriverName => Info.DriverName;

    public string Kind => Info.IsMock ? "Mock" : "Hardware";
}
