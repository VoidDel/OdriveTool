using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdriveUpper.Core.Devices;
using OdriveUpper.Core.Telemetry;
using OdriveUpper.Drivers.Mock;

namespace OdriveUpper.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDeviceDriver _driver = new MockOdriveDriver();
    private CancellationTokenSource? _telemetryCts;
    private IDeviceSession? _session;

    public MainWindowViewModel()
    {
        _ = RefreshDevicesAsync();
    }

    public ObservableCollection<DeviceListItemViewModel> Devices { get; } = [];

    public ObservableCollection<string> Capabilities { get; } = [];

    public ObservableCollection<TelemetryValueViewModel> TelemetryValues { get; } = [];

    [ObservableProperty]
    private DeviceListItemViewModel? _selectedDevice;

    [ObservableProperty]
    private string _connectionStatus = "正在初始化新上位机...";

    [ObservableProperty]
    private string _schemaName = "未加载";

    [ObservableProperty]
    private string _firmwareSource = @"自定义 ODrive 固件源码：D:\xiafeng\Project\Odrive\ODrive\Firmware";

    [ObservableProperty]
    private string _lastCommandResult = "等待命令";

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        Devices.Clear();
        ConnectionStatus = $"正在通过 {_driver.Name} 扫描设备...";

        var devices = await _driver.DiscoverAsync(CancellationToken.None);
        foreach (var device in devices)
        {
            Devices.Add(new DeviceListItemViewModel(device));
        }

        SelectedDevice = Devices.FirstOrDefault();
        ConnectionStatus = Devices.Count == 0 ? "未发现设备" : $"发现 {Devices.Count} 个设备";

        if (SelectedDevice is not null)
        {
            await ConnectSelectedDeviceAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectSelectedDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            ConnectionStatus = "请选择设备";
            return;
        }

        _telemetryCts?.Cancel();
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        ConnectionStatus = $"正在连接 {SelectedDevice.SerialNumber}...";
        _session = await _driver.ConnectAsync(SelectedDevice.SerialNumber, CancellationToken.None);

        var capabilities = await _session.GetCapabilitiesAsync(CancellationToken.None);
        Capabilities.Clear();
        Capabilities.Add($"自定义固件扩展：{ToYesNo(capabilities.SupportsCustomFirmwareExtensions)}");
        Capabilities.Add($"官方 ODrive 兼容层：{ToYesNo(capabilities.SupportsOfficialOdriveCompatibility)}");
        Capabilities.Add($"多摩川编码器：{ToYesNo(capabilities.SupportsTamagawaEncoder)}");
        Capabilities.Add($"实时遥测：{ToYesNo(capabilities.SupportsLiveTelemetry)}");
        Capabilities.Add($"保存配置：{ToYesNo(capabilities.SupportsConfigurationSave)}");
        Capabilities.Add($"编码器类型：{string.Join(", ", capabilities.EncoderTypes)}");
        Capabilities.Add($"特性标记：{string.Join(", ", capabilities.CustomFeatureFlags)}");

        var schema = await _session.GetApiSchemaAsync(CancellationToken.None);
        SchemaName = $"{schema.DisplayName} / schema v{schema.Version}";

        ConnectionStatus = $"已连接：{_session.Info.DisplayName}";
        StartTelemetry(_session);
    }

    [RelayCommand]
    private async Task RunTamagawaSelfTestAsync()
    {
        if (_session is null)
        {
            LastCommandResult = "设备未连接";
            return;
        }

        var result = await _session.InvokeAsync("axis0.tamagawa.run_self_test", [], CancellationToken.None);
        LastCommandResult = result.Success ? Convert.ToString(result.Result) ?? "命令执行成功" : result.Error ?? "命令执行失败";
    }

    private void StartTelemetry(IDeviceSession session)
    {
        _telemetryCts = new CancellationTokenSource();
        var subscription = new TelemetrySubscription(
            [
                "vbus_voltage",
                "axis0.encoder.pos_estimate",
                "axis0.encoder.vel_estimate",
                "axis0.motor.current_control.iq_measured",
                "axis0.tamagawa.crc_error_count"
            ],
            TimeSpan.FromMilliseconds(250));

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in session.SubscribeTelemetryAsync(subscription, _telemetryCts.Token))
                {
                    Dispatcher.UIThread.Post(() => UpdateTelemetry(frame));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when reconnecting or closing the app.
            }
        });
    }

    private void UpdateTelemetry(TelemetryFrame frame)
    {
        foreach (var (path, value) in frame.Values)
        {
            var existing = TelemetryValues.FirstOrDefault(item => item.Path == path);
            var text = FormatTelemetryValue(value);
            if (existing is null)
            {
                TelemetryValues.Add(new TelemetryValueViewModel(path, text));
            }
            else
            {
                existing.Value = text;
            }
        }
    }

    private static string FormatTelemetryValue(object? value) => value switch
    {
        null => "—",
        double d => d.ToString("0.####"),
        float f => f.ToString("0.####"),
        _ => Convert.ToString(value) ?? "—"
    };

    private static string ToYesNo(bool value) => value ? "支持" : "不支持";
}
