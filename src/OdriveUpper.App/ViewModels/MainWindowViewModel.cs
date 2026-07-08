using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdriveUpper.Core.Devices;
using OdriveUpper.Core.Firmware;
using OdriveUpper.Core.Telemetry;
using OdriveUpper.Drivers.Mock;
using OdriveUpper.Drivers.Serial;

namespace OdriveUpper.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IReadOnlyList<IDeviceDriver> _drivers =
    [
        new SerialAsciiOdriveDriver(),
        new MockOdriveDriver()
    ];

    private CancellationTokenSource? _telemetryCts;
    private IDeviceSession? _session;
    private DeviceApiSchema? _schema;

    public MainWindowViewModel()
    {
        _ = RefreshDevicesAsync();
    }

    public ObservableCollection<DeviceListItemViewModel> Devices { get; } = [];

    public ObservableCollection<string> Capabilities { get; } = [];

    public ObservableCollection<TelemetryValueViewModel> TelemetryValues { get; } = [];

    public ObservableCollection<ParameterNodeViewModel> ParameterTree { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadSelectedParameterCommand))]
    [NotifyCanExecuteChangedFor(nameof(WriteSelectedParameterCommand))]
    private ParameterNodeViewModel? _selectedParameterNode;

    [ObservableProperty]
    private string _parameterStatus = "连接设备后加载参数树";

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

    [ObservableProperty]
    private string _vbusVoltageText = "—";

    [ObservableProperty]
    private string _iqMeasuredText = "—";

    [ObservableProperty]
    private string _encoderPositionText = "—";

    [ObservableProperty]
    private string _encoderVelocityRpmText = "—";

    [ObservableProperty]
    private string _tamagawaCrcErrorCountText = "0";

    [ObservableProperty]
    private string _controllerLoopStatus = "等待遥测";

    [ObservableProperty]
    private string _tamagawaAbsolutePositionText = "—";

    [ObservableProperty]
    private string _tamagawaMultiTurnText = "—";

    // --- UI Navigation and Theme ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardActive))]
    [NotifyPropertyChangedFor(nameof(IsControlActive))]
    [NotifyPropertyChangedFor(nameof(IsEncoderActive))]
    [NotifyPropertyChangedFor(nameof(IsSystemActive))]
    private string _selectedTab = "Dashboard";

    public bool IsDashboardActive => SelectedTab == "Dashboard";
    public bool IsControlActive => SelectedTab == "Control";
    public bool IsEncoderActive => SelectedTab == "Encoder";
    public bool IsSystemActive => SelectedTab == "System";

    [ObservableProperty]
    private string _currentThemeName = "暗色";

    // --- Control and Tuning Parameters ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPositionMode))]
    [NotifyPropertyChangedFor(nameof(IsVelocityMode))]
    [NotifyPropertyChangedFor(nameof(IsTorqueMode))]
    private string _controlMode = "位置控制"; // 位置控制, 速度控制, 力矩控制

    public bool IsPositionMode => ControlMode == "位置控制";
    public bool IsVelocityMode => ControlMode == "速度控制";
    public bool IsTorqueMode => ControlMode == "力矩控制";

    [ObservableProperty]
    private double _targetPosition = 0.0;

    [ObservableProperty]
    private double _targetVelocity = 0.0;

    [ObservableProperty]
    private double _targetTorque = 0.0;

    [ObservableProperty]
    private bool _isMotorEnabled = false;

    [ObservableProperty]
    private double _posGain = 20.0;

    [ObservableProperty]
    private double _velGain = 0.0005;

    [ObservableProperty]
    private double _velIntegratorGain = 0.001;

    // --- Encoder Status ---
    [ObservableProperty]
    private string _encoderWarning = "无报警";

    [ObservableProperty]
    private string _encoderError = "无错误";

    partial void OnIsMotorEnabledChanged(bool value)
    {
        _ = WriteSingleAsync("axis0.requested_state", value ? "ClosedLoopControl" : "Idle");
        ControllerLoopStatus = value ? "ACTIVE" : "IDLE";
    }

    partial void OnTargetPositionChanged(double value)
    {
        if (IsPositionMode)
        {
            _ = WriteSingleAsync("axis0.controller.input_pos", value);
        }
    }

    partial void OnTargetVelocityChanged(double value)
    {
        if (IsVelocityMode)
        {
            _ = WriteSingleAsync("axis0.controller.input_vel", value);
        }
    }

    partial void OnTargetTorqueChanged(double value)
    {
        if (IsTorqueMode)
        {
            _ = WriteSingleAsync("axis0.controller.input_torque", value);
        }
    }

    partial void OnPosGainChanged(double value) => _ = WriteSingleAsync("axis0.controller.config.pos_gain", value);

    partial void OnVelGainChanged(double value) => _ = WriteSingleAsync("axis0.controller.config.vel_gain", value);

    partial void OnVelIntegratorGainChanged(double value) => _ = WriteSingleAsync("axis0.controller.config.vel_integrator_gain", value);

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Application.Current is not null)
        {
            var current = Application.Current.RequestedThemeVariant;
            if (current == ThemeVariant.Dark)
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                CurrentThemeName = "亮色";
            }
            else
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                CurrentThemeName = "暗色";
            }
        }
    }

    [RelayCommand]
    private void SetTab(string tabName)
    {
        SelectedTab = tabName;
    }

    [RelayCommand]
    private async Task CalibrateMotorAsync()
    {
        await InvokeCommandAsync("axis0.run_motor_calibration", "执行电机标定");
    }

    [RelayCommand]
    private async Task ClearEncoderWarningAsync()
    {
        var ok = await InvokeCommandAsync("axis0.tamagawa.clear_alarm", "清除多摩川编码器报警");
        if (ok)
        {
            EncoderWarning = "无报警";
            EncoderError = "无错误";
        }
    }

    [RelayCommand]
    private async Task CalibrateEncoderZeroAsync()
    {
        await InvokeCommandAsync("axis0.tamagawa.calibrate_zero", "校准多摩川机械零点");
    }

    [RelayCommand]
    private async Task SaveConfigurationAsync()
    {
        await InvokeCommandAsync("save_configuration", "保存配置至 ODrive 闪存");
    }
    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        Devices.Clear();
        ConnectionStatus = "正在扫描真实 ODrive 和 Mock 设备...";

        foreach (var driver in _drivers)
        {
            try
            {
                var devices = await driver.DiscoverAsync(CancellationToken.None);
                foreach (var device in devices)
                {
                    Devices.Add(new DeviceListItemViewModel(device, driver));
                }
            }
            catch (Exception ex)
            {
                LastCommandResult = $"{driver.Name} 扫描失败：{ex.Message}";
            }
        }

        SelectedDevice = Devices.FirstOrDefault(device => !device.Info.IsMock) ?? Devices.FirstOrDefault();
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
        _session = await SelectedDevice.Driver.ConnectAsync(SelectedDevice.SerialNumber, CancellationToken.None);

        var capabilities = await _session.GetCapabilitiesAsync(CancellationToken.None);
        Capabilities.Clear();
        Capabilities.Add($"自定义固件扩展：{ToYesNo(capabilities.SupportsCustomFirmwareExtensions)}");
        Capabilities.Add($"官方 ODrive 兼容层：{ToYesNo(capabilities.SupportsOfficialOdriveCompatibility)}");
        Capabilities.Add($"多摩川编码器：{ToYesNo(capabilities.SupportsTamagawaEncoder)}");
        Capabilities.Add($"实时遥测：{ToYesNo(capabilities.SupportsLiveTelemetry)}");
        Capabilities.Add($"保存配置：{ToYesNo(capabilities.SupportsConfigurationSave)}");
        Capabilities.Add($"编码器类型：{string.Join(", ", capabilities.EncoderTypes)}");
        Capabilities.Add($"特性标记：{string.Join(", ", capabilities.CustomFeatureFlags)}");

        _schema = await _session.GetApiSchemaAsync(CancellationToken.None);
        SchemaName = $"{_schema.DisplayName} / schema v{_schema.Version}";
        BuildParameterTree(_schema);

        ConnectionStatus = $"已连接：{_session.Info.DisplayName}";
        StartTelemetry(_session);
    }

    [RelayCommand(CanExecute = nameof(CanReadSelectedParameter))]
    private async Task ReadSelectedParameterAsync()
    {
        if (_session is null || SelectedParameterNode?.Property is null)
        {
            return;
        }

        await ReadParameterAsync(SelectedParameterNode);
    }

    [RelayCommand(CanExecute = nameof(CanWriteSelectedParameter))]
    private async Task WriteSelectedParameterAsync()
    {
        if (_session is null || SelectedParameterNode?.Property is null)
        {
            return;
        }

        var node = SelectedParameterNode;
        node.Status = "正在写入...";
        var converted = ConvertPendingValue(node.PendingValue, node.ValueType);
        var results = await _session.WriteAsync([new PropertyWrite(node.Path, converted)], CancellationToken.None);
        var result = results.FirstOrDefault();
        if (result is { Success: true })
        {
            node.Status = "写入成功，正在回读...";
            await ReadParameterAsync(node);
        }
        else
        {
            node.Status = $"写入失败：{result?.Error ?? "未知错误"}";
        }
    }

    [RelayCommand]
    private async Task ReadAllParametersAsync()
    {
        if (_session is null)
        {
            ParameterStatus = "设备未连接";
            return;
        }

        var scope = SelectedParameterNode is not null
            ? [SelectedParameterNode]
            : ParameterTree;
        var leaves = Flatten(scope).Where(node => node.Property is not null).ToArray();
        if (leaves.Length == 0)
        {
            ParameterStatus = "请选择一个包含参数的节点";
            return;
        }

        ParameterStatus = $"正在读取 {leaves.Length} 个参数...";

        foreach (var node in leaves)
        {
            await ReadParameterAsync(node);
        }

        ParameterStatus = $"已读取 {leaves.Length} 个参数";
    }

    [RelayCommand]
    private async Task RunTamagawaSelfTestAsync()
    {
        if (_session is null)
        {
            LastCommandResult = "设备未连接";
            return;
        }

        await InvokeCommandAsync("axis0.tamagawa.run_self_test", "多摩川编码器自检");
    }

    private bool CanReadSelectedParameter() => _session is not null && SelectedParameterNode?.Property is not null;

    private bool CanWriteSelectedParameter() =>
        _session is not null && SelectedParameterNode?.Property is { IsWritable: true };

    private void BuildParameterTree(DeviceApiSchema schema)
    {
        ParameterTree.Clear();
        SelectedParameterNode = null;

        foreach (var property in schema.Properties.OrderBy(property => property.Path, StringComparer.OrdinalIgnoreCase))
        {
            AddPropertyNode(property);
        }

        ParameterStatus = $"已加载 {schema.Properties.Count} 个参数";
    }

    private void AddPropertyNode(ApiProperty property)
    {
        var parts = property.Path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var collection = ParameterTree;
        var currentPath = string.Empty;
        ParameterNodeViewModel? current = null;

        foreach (var part in parts)
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}.{part}";
            current = collection.FirstOrDefault(node => node.Name == part);
            if (current is null)
            {
                current = new ParameterNodeViewModel(part, currentPath);
                collection.Add(current);
            }

            collection = current.Children;
        }

        current?.AttachProperty(property);
    }

    private async Task ReadParameterAsync(ParameterNodeViewModel node)
    {
        if (_session is null || node.Property is null)
        {
            return;
        }

        node.Status = "正在读取...";
        var result = await _session.ReadAsync([node.Path], CancellationToken.None);
        if (result.Values.TryGetValue(node.Path, out var value))
        {
            var text = FormatTelemetryValue(value);
            node.CurrentValue = text;
            node.PendingValue = text;
            node.Status = "读取成功";
        }
        else
        {
            node.Status = result.Errors.TryGetValue(node.Path, out var error) ? $"读取失败：{error}" : "读取失败";
        }
    }

    private static IEnumerable<ParameterNodeViewModel> Flatten(IEnumerable<ParameterNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static object? ConvertPendingValue(string value, string valueType)
    {
        if (valueType.Contains("bool", StringComparison.OrdinalIgnoreCase))
        {
            return value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        if (valueType.Contains("int", StringComparison.OrdinalIgnoreCase) ||
            valueType.Contains("uint", StringComparison.OrdinalIgnoreCase) ||
            valueType.Contains("enum", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : value;
        }

        if (valueType.Contains("float", StringComparison.OrdinalIgnoreCase) ||
            valueType.Contains("double", StringComparison.OrdinalIgnoreCase))
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : value;
        }

        return value;
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
                "axis0.tamagawa.crc_error_count",
                "axis0.tamagawa.absolute_position",
                "axis0.tamagawa.multi_turn_count",
                "axis0.tamagawa.warning_status",
                "axis0.tamagawa.error_status"
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
            UpdateTelemetrySummary(path, value);

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

    private void UpdateTelemetrySummary(string path, object? value)
    {
        switch (path)
        {
            case "vbus_voltage":
                VbusVoltageText = $"{ToDouble(value):0.00} V";
                break;
            case "axis0.motor.current_control.iq_measured":
                IqMeasuredText = $"{ToDouble(value):0.00} A";
                break;
            case "axis0.encoder.pos_estimate":
                EncoderPositionText = $"{ToDouble(value):0.000} Turns";
                break;
            case "axis0.encoder.vel_estimate":
                EncoderVelocityRpmText = $"{ToDouble(value) * 60.0:0.0} RPM";
                break;
            case "axis0.tamagawa.crc_error_count":
                TamagawaCrcErrorCountText = FormatTelemetryValue(value);
                break;
            case "axis0.tamagawa.absolute_position":
                TamagawaAbsolutePositionText = $"{Convert.ToInt64(value):N0} / 131,072";
                break;
            case "axis0.tamagawa.multi_turn_count":
                TamagawaMultiTurnText = $"{Convert.ToInt64(value)} 圈";
                break;
            case "axis0.tamagawa.warning_status":
                EncoderWarning = Convert.ToInt64(value) == 0 ? "无报警" : $"报警字 0x{Convert.ToInt64(value):X2}";
                break;
            case "axis0.tamagawa.error_status":
                EncoderError = Convert.ToInt64(value) == 0 ? "无错误" : $"错误字 0x{Convert.ToInt64(value):X2}";
                break;
        }
    }

    private async Task WriteSingleAsync(string path, object? value)
    {
        if (_session is null)
        {
            return;
        }

        var results = await _session.WriteAsync([new PropertyWrite(path, value)], CancellationToken.None);
        var result = results.FirstOrDefault();
        if (result is { Success: false })
        {
            LastCommandResult = $"写入失败 {path}: {result.Error}";
        }
    }

    private async Task<bool> InvokeCommandAsync(string path, string displayName)
    {
        if (_session is null)
        {
            LastCommandResult = "设备未连接";
            return false;
        }

        LastCommandResult = $"{displayName}...";
        var result = await _session.InvokeAsync(path, [], CancellationToken.None);
        LastCommandResult = result.Success
            ? $"{displayName}完成：{Convert.ToString(result.Result) ?? "OK"}"
            : $"{displayName}失败：{result.Error ?? "未知错误"}";
        return result.Success;
    }

    private static string FormatTelemetryValue(object? value) => value switch
    {
        null => "—",
        double d => d.ToString("0.####"),
        float f => f.ToString("0.####"),
        _ => Convert.ToString(value) ?? "—"
    };

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        _ => 0.0
    };

    private static string ToYesNo(bool value) => value ? "支持" : "不支持";
}
