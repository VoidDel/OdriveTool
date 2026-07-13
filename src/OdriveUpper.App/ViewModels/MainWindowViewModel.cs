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
using OdriveUpper.Drivers.Serial;

namespace OdriveUpper.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int AxisStateIdle = 1;
    private const int AxisStateMotorCalibration = 4;
    private const int AxisStateClosedLoopControl = 8;
    private const int ControlModeTorque = 1;
    private const int ControlModeVelocity = 2;
    private const int ControlModePosition = 3;

    private const string IconCircleHalf = "M 8 15 A 7 7 0 1 0 8 1 Z M 8 16 A 8 8 0 1 1 8 0 A 8 8 0 0 1 8 16 Z";
    private const string IconSun = "M 8 12 a 4 4 0 1 0 0 -8 a 4 4 0 0 0 0 8 M 8 0 a 0.5 0.5 0 0 1 0.5 0.5 v 2 a 0.5 0.5 0 0 1 -1 0 v -2 A 0.5 0.5 0 0 1 8 0 M 8 13 a 0.5 0.5 0 0 1 0.5 0.5 v 2 a 0.5 0.5 0 0 1 -1 0 v -2 A 0.5 0.5 0 0 1 8 13 M 16 8 a 0.5 0.5 0 0 1 -0.5 0.5 h -2 a 0.5 0.5 0 0 1 0 -1 h 2 a 0.5 0.5 0 0 1 0.5 0.5 M 3 8 a 0.5 0.5 0 0 1 -0.5 0.5 h -2 a 0.5 0.5 0 0 1 0 -1 h 2 A 0.5 0.5 0 0 1 3 8 M 13.657 2.343 a 0.5 0.5 0 0 1 0 0.707 l -1.414 1.415 a 0.5 0.5 0 1 1 -0.707 -0.708 l 1.414 -1.414 a 0.5 0.5 0 0 1 0.707 0 M 4.464 11.536 a 0.5 0.5 0 0 1 0 0.707 l -1.414 1.414 a 0.5 0.5 0 0 1 -0.707 -0.707 l 1.414 -1.414 a 0.5 0.5 0 0 1 0.707 0 M 13.657 13.657 a 0.5 0.5 0 0 1 -0.707 0 l -1.414 -1.414 a 0.5 0.5 0 0 1 0.707 -0.707 l 1.414 1.414 a 0.5 0.5 0 0 1 0 0.707 M 4.464 4.465 a 0.5 0.5 0 0 1 -0.707 0 l -1.414 -1.414 a 0.5 0.5 0 1 1 0.707 -0.707 l 1.414 1.414 a 0.5 0.5 0 0 1 0 0.708";
    private const string IconMoon = "M 6 0.278 a 0.768 0.768 0 0 1 0.08 0.858 a 7.208 7.208 0 0 0 -0.878 3.46 c 0 4.021 3.278 7.277 7.318 7.277 c 0.527 0 1.04 -0.055 1.533 -0.16 a 0.787 0.787 0 0 1 0.81 0.316 a 0.733 0.733 0 0 1 -0.031 0.893 A 8.349 8.349 0 0 1 8.344 16 C 3.734 16 0 12.286 0 7.71 C 0 4.266 2.114 1.312 5.124 0.06 A 0.752 0.752 0 0 1 6 0.278 z";

    private const string IconDashboardPath = "M 6 2 a 0.5 0.5 0 0 1 0.47 0.33 L 10 12.036 l 1.53 -4.208 A 0.5 0.5 0 0 1 12 7.5 h 3.5 a 0.5 0.5 0 0 1 0 1 h -3.15 l -1.88 5.17 a 0.5 0.5 0 0 1 -0.94 0 L 6 3.964 L 4.47 8.171 A 0.5 0.5 0 0 1 4 8.5 H 0.5 a 0.5 0.5 0 0 1 0 -1 h 3.15 l 1.88 -5.17 A 0.5 0.5 0 0 1 6 2";
    private const string IconControlPath = "M 10.5 1 a 0.5 0.5 0 0 1 0.5 0.5 v 4 a 0.5 0.5 0 0 1 -1 0 V 4 H 1.5 a 0.5 0.5 0 0 1 0 -1 H 10 V 1.5 a 0.5 0.5 0 0 1 0.5 -0.5 M 12 3.5 a 0.5 0.5 0 0 1 0.5 -0.5 h 2 a 0.5 0.5 0 0 1 0 1 h -2 a 0.5 0.5 0 0 1 -0.5 -0.5 m -6.5 2 A 0.5 0.5 0 0 1 6 6 v 1.5 h 8.5 a 0.5 0.5 0 0 1 0 1 H 6 V 10 a 0.5 0.5 0 0 1 -1 0 V 6 a 0.5 0.5 0 0 1 0.5 -0.5 M 1 8 a 0.5 0.5 0 0 1 0.5 -0.5 h 2 a 0.5 0.5 0 0 1 0 1 h -2 A 0.5 0.5 0 0 1 1 8 m 9.5 2 a 0.5 0.5 0 0 1 0.5 0.5 v 4 a 0.5 0.5 0 0 1 -1 0 V 13 H 1.5 a 0.5 0.5 0 0 1 0 -1 H 10 v -1.5 a 0.5 0.5 0 0 1 0.5 -0.5 m 1.5 2.5 a 0.5 0.5 0 0 1 0.5 -0.5 h 2 a 0.5 0.5 0 0 1 0 1 h -2 a 0.5 0.5 0 0 1 -0.5 -0.5";
    private const string IconMotorPath = "M 6 1 h 4 v 2 h 2 a 2 2 0 0 1 2 2 v 6 a 2 2 0 0 1 -2 2 h -2 v 2 H 6 v -2 H 4 a 2 2 0 0 1 -2 -2 V 5 a 2 2 0 0 1 2 -2 h 2 V 1 Z M 5 5 a 1 1 0 0 0 -1 1 v 4 a 1 1 0 0 0 1 1 h 6 a 1 1 0 0 0 1 -1 V 6 a 1 1 0 0 0 -1 -1 H 5 Z M 6 7 h 4 v 2 H 6 V 7 Z";
    private const string IconEncoderPath = "M 8 15 A 7 7 0 1 0 8 1 Z M 8 16 A 8 8 0 1 1 8 0 A 8 8 0 0 1 8 16 Z M 8 10 A 2 2 0 1 0 8 6 A 2 2 0 0 0 8 10 Z M 8 11 A 3 3 0 1 1 8 5 A 3 3 0 0 1 8 11 Z";
    private const string IconGuidePath = "M 3 2 h 10 a 1 1 0 0 1 1 1 v 10 a 1 1 0 0 1 -1 1 H 3 a 1 1 0 0 1 -1 -1 V 3 a 1 1 0 0 1 1 -1 M 4 4 v 8 h 8 V 4 H 4 M 5 5 h 6 v 1 H 5 V 5 M 5 7 h 6 v 1 H 5 V 7 M 5 9 h 4 v 1 H 5 V 9";
    private const string IconSystemPath = "M 14 2 H 6 c -1.1 0 -1.99 0.9 -1.99 2 L 4 20 c 0 1.1 0.89 2 1.99 2 H 18 c 1.1 0 2 -0.9 2 -2 V 8 l -6 -6 z M 16 18 H 8 v -2 h 8 v 2 z M 16 14 H 8 v -2 h 8 v 2 z M 13 9 V 3.5 L 18.5 9 H 13 z";
    private const string IconChevronLeftPath = "M 11.354 1.646 a 0.5 0.5 0 0 1 0 0.708 L 5.707 8 l 5.647 5.646 a 0.5 0.5 0 0 1 -0.708 0.708 l -6 -6 a 0.5 0.5 0 0 1 0 -0.708 l 6 -6 a 0.5 0.5 0 0 1 0.708 0";
    private const string IconChevronRightPath = "M 4.646 1.646 a 0.5 0.5 0 0 1 0.708 0 l 6 6 a 0.5 0.5 0 0 1 0 0.708 l -6 6 a 0.5 0.5 0 0 1 -0.708 -0.708 L 10.293 8 L 4.646 2.354 a 0.5 0.5 0 0 1 0 -0.708";

    private readonly IReadOnlyList<IDeviceDriver> _drivers =
    [
        new SerialAsciiOdriveDriver()
    ];

    private CancellationTokenSource? _telemetryCts;
    private IDeviceSession? _session;
    private DeviceApiSchema? _schema;

    public MainWindowViewModel()
    {
        SetupWizard = new SetupWizardViewModel(
            ReadWizardValuesAsync,
            ApplyWizardWritesAsync,
            SaveConfigurationAsync,
            RequestWizardAxisStateAsync);
        _ = RefreshDevicesAsync();
    }

    public SetupWizardViewModel SetupWizard { get; }

    public ObservableCollection<DeviceListItemViewModel> Devices { get; } = [];

    public ObservableCollection<string> Capabilities { get; } = [];

    public ObservableCollection<int> AvailableAxes { get; } = [0, 1];

    public ObservableCollection<string> AvailableControlModes { get; } = ["位置控制", "速度控制", "力矩控制"];

    public ObservableCollection<TelemetryValueViewModel> TelemetryValues { get; } = [];

    public ObservableCollection<ParameterNodeViewModel> ParameterTree { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadSelectedParameterCommand))]
    [NotifyCanExecuteChangedFor(nameof(WriteSelectedParameterCommand))]
    private ParameterNodeViewModel? _selectedParameterNode;

    [ObservableProperty]
    private string _parameterStatus = "连接设备后加载参数树";

    [ObservableProperty]
    private string _parameterSearchText = string.Empty;

    [ObservableProperty]
    private bool _showWritableOnly;

    partial void OnParameterSearchTextChanged(string value) => RebuildParameterTreeFromCurrentSchema();

    partial void OnShowWritableOnlyChanged(bool value) => RebuildParameterTreeFromCurrentSchema();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendTargetCommand))]
    private bool _isDeviceBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedDeviceCommand))]
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
    private int _selectedAxis = 0;

    [ObservableProperty]
    private string _axisStateText = "—";

    [ObservableProperty]
    private string _axisErrorText = "—";

    [ObservableProperty]
    private string _motorErrorText = "—";

    [ObservableProperty]
    private string _encoderErrorText = "—";

    [ObservableProperty]
    private string _controllerErrorText = "—";

    [ObservableProperty]
    private string _vbusVoltageText = "—";

    [ObservableProperty]
    private string _ibusText = "—";

    [ObservableProperty]
    private string _iqMeasuredText = "—";

    [ObservableProperty]
    private string _iqSetpointText = "—";

    [ObservableProperty]
    private string _motorTemperatureText = "—";

    [ObservableProperty]
    private string _fetTemperatureText = "—";

    [ObservableProperty]
    private string _motorArmedText = "—";

    [ObservableProperty]
    private string _motorCalibratedText = "—";

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

    [ObservableProperty]
    private string _encoderReadyText = "—";

    [ObservableProperty]
    private string _encoderIndexFoundText = "—";

    [ObservableProperty]
    private string _encoderShadowCountText = "—";

    [ObservableProperty]
    private string _encoderCountInCprText = "—";

    // --- UI Navigation and Theme ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardActive))]
    [NotifyPropertyChangedFor(nameof(IsControlActive))]
    [NotifyPropertyChangedFor(nameof(IsMotorActive))]
    [NotifyPropertyChangedFor(nameof(IsEncoderActive))]
    [NotifyPropertyChangedFor(nameof(IsGuideActive))]
    [NotifyPropertyChangedFor(nameof(IsSystemActive))]
    [NotifyPropertyChangedFor(nameof(SelectedTabTitle))]
    private string _selectedTab = "Dashboard";

    public bool IsDashboardActive => SelectedTab == "Dashboard";
    public bool IsControlActive => SelectedTab == "Control";
    public bool IsMotorActive => SelectedTab == "Motor";
    public bool IsEncoderActive => SelectedTab == "Encoder";
    public bool IsGuideActive => SelectedTab == "Guide";
    public bool IsSystemActive => SelectedTab == "System";

    public string SelectedTabTitle => SelectedTab switch
    {
        "Dashboard" => "实时遥测",
        "Control" => "控制模式与给定",
        "Motor" => "电机与标定",
        "Encoder" => "编码器反馈",
        "Guide" => "设置指引",
        "System" => "固件与 Schema",
        _ => SelectedTab
    };

    public string IconDashboard => IconDashboardPath;
    public string IconControl => IconControlPath;
    public string IconMotor => IconMotorPath;
    public string IconEncoder => IconEncoderPath;
    public string IconGuide => IconGuidePath;
    public string IconSystem => IconSystemPath;
    public string IconChevronLeft => IconChevronLeftPath;
    public string IconChevronRight => IconChevronRightPath;

    [ObservableProperty]
    private string _currentThemeName = "跟随系统";

    [ObservableProperty]
    private string _currentThemeIcon = IconCircleHalf;

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

    // --- Motor Parameters ---
    [ObservableProperty]
    private int _motorType = 0; // 0: High Current, 2: Gimbal, 3: ACIM

    [ObservableProperty]
    private int _polePairs = 7;

    [ObservableProperty]
    private double _torqueConstant = 0.04;

    [ObservableProperty]
    private double _currentLim = 10.0;

    [ObservableProperty]
    private double _calibrationCurrent = 10.0;

    [ObservableProperty]
    private double _resistanceCalibMaxVoltage = 2.0;

    [ObservableProperty]
    private double _phaseResistance = 0.0;

    [ObservableProperty]
    private double _phaseInductance = 0.0;

    [ObservableProperty]
    private bool _isMotorPreCalibrated = false;

    // --- Encoder Status ---
    [ObservableProperty]
    private string _encoderWarning = "无报警";

    [ObservableProperty]
    private string _encoderError = "无错误";

    partial void OnSelectedAxisChanged(int value)
    {
        _ = RefreshSelectedAxisStatusAsync();
    }

    partial void OnIsMotorEnabledChanged(bool value)
    {
        _ = WriteSingleAsync(AxisPath("requested_state"), value ? AxisStateClosedLoopControl : AxisStateIdle);
        ControllerLoopStatus = value ? "ACTIVE" : "IDLE";
    }

    partial void OnControlModeChanged(string value)
    {
        var mode = value switch
        {
            "位置控制" => ControlModePosition,
            "速度控制" => ControlModeVelocity,
            "力矩控制" => ControlModeTorque,
            _ => ControlModePosition
        };

        _ = WriteSingleAsync(AxisPath("controller.config.control_mode"), mode);
    }

    [RelayCommand(CanExecute = nameof(CanRunDeviceOperation))]
    private Task SendTargetAsync() => ControlMode switch
    {
        "位置控制" => WriteManyAsync(
            [new PropertyWrite(AxisPath("controller.input_pos"), TargetPosition)],
            $"设置目标位置 {TargetPosition:F2} Turns"),
        "速度控制" => WriteManyAsync(
            [new PropertyWrite(AxisPath("controller.input_vel"), TargetVelocity / 60.0)],
            $"设置目标速度 {TargetVelocity:F1} RPM"),
        "力矩控制" => WriteManyAsync(
            [new PropertyWrite(AxisPath("controller.input_torque"), TargetTorque)],
            $"设置目标力矩 {TargetTorque:F2} Nm"),
        _ => Task.CompletedTask
    };

    partial void OnPosGainChanged(double value) => _ = WriteSingleAsync(AxisPath("controller.config.pos_gain"), value);

    partial void OnVelGainChanged(double value) => _ = WriteSingleAsync(AxisPath("controller.config.vel_gain"), value);

    partial void OnVelIntegratorGainChanged(double value) => _ = WriteSingleAsync(AxisPath("controller.config.vel_integrator_gain"), value);

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Application.Current is not null)
        {
            var current = Application.Current.RequestedThemeVariant;
            if (current == ThemeVariant.Default)
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                CurrentThemeName = "亮色";
                CurrentThemeIcon = IconSun;
            }
            else if (current == ThemeVariant.Light)
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                CurrentThemeName = "暗色";
                CurrentThemeIcon = IconMoon;
            }
            else
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Default;
                CurrentThemeName = "跟随系统";
                CurrentThemeIcon = IconCircleHalf;
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
        await WriteAxisStateAsync(AxisStateMotorCalibration, "执行电机标定");
    }

    [RelayCommand]
    private async Task DisableMotorAsync()
    {
        IsMotorEnabled = false;
        await WriteAxisStateAsync(AxisStateIdle, "切换到 Idle");
    }

    [RelayCommand]
    private async Task EmergencyStopAsync()
    {
        IsMotorEnabled = false;
        var writes = AvailableAxes
            .Select(axis => new PropertyWrite($"axis{axis}.requested_state", AxisStateIdle))
            .ToArray();
        await WriteManyAsync(writes, "紧急制动：所有轴切换到 Idle");
    }

    [RelayCommand]
    private async Task ClearErrorsAsync()
    {
        if (await InvokeCommandAsync("clear_errors", "清除设备错误"))
        {
            await RefreshSelectedAxisStatusAsync();
        }
    }

    [RelayCommand]
    public async Task ReadMotorParametersAsync()
    {
        if (_session is null)
        {
            LastCommandResult = "设备未连接，无法读取电机参数";
            return;
        }

        LastCommandResult = "正在读取电机参数...";
        var res = await _session.ReadAsync([
            "axis0.motor.config.motor_type",
            "axis0.motor.config.pole_pairs",
            "axis0.motor.config.torque_constant",
            "axis0.motor.config.current_lim",
            "axis0.motor.config.calibration_current",
            "axis0.motor.config.resistance_calib_max_voltage",
            "axis0.motor.config.phase_resistance",
            "axis0.motor.config.phase_inductance",
            "axis0.motor.config.pre_calibrated"
        ], CancellationToken.None);

        if (res.Values.TryGetValue("axis0.motor.config.motor_type", out var motorType))
            MotorType = Convert.ToInt32(motorType);
        if (res.Values.TryGetValue("axis0.motor.config.pole_pairs", out var polePairs))
            PolePairs = Convert.ToInt32(polePairs);
        if (res.Values.TryGetValue("axis0.motor.config.torque_constant", out var torqueConstant))
            TorqueConstant = ToDouble(torqueConstant);
        if (res.Values.TryGetValue("axis0.motor.config.current_lim", out var currentLim))
            CurrentLim = ToDouble(currentLim);
        if (res.Values.TryGetValue("axis0.motor.config.calibration_current", out var calibCurrent))
            CalibrationCurrent = ToDouble(calibCurrent);
        if (res.Values.TryGetValue("axis0.motor.config.resistance_calib_max_voltage", out var calibVoltage))
            ResistanceCalibMaxVoltage = ToDouble(calibVoltage);
        if (res.Values.TryGetValue("axis0.motor.config.phase_resistance", out var phaseRes))
            PhaseResistance = ToDouble(phaseRes);
        if (res.Values.TryGetValue("axis0.motor.config.phase_inductance", out var phaseInd))
            PhaseInductance = ToDouble(phaseInd);
        if (res.Values.TryGetValue("axis0.motor.config.pre_calibrated", out var preCalib))
            IsMotorPreCalibrated = preCalib is bool b ? b : (Convert.ToInt32(preCalib) != 0);

        LastCommandResult = "电机参数读取成功";
    }

    [RelayCommand]
    public async Task WriteMotorParametersAsync()
    {
        if (_session is null)
        {
            LastCommandResult = "设备未连接，无法写入电机参数";
            return;
        }

        LastCommandResult = "正在写入电机参数...";
        var writes = new List<PropertyWrite>
        {
            new("axis0.motor.config.motor_type", MotorType),
            new("axis0.motor.config.pole_pairs", PolePairs),
            new("axis0.motor.config.torque_constant", TorqueConstant),
            new("axis0.motor.config.current_lim", CurrentLim),
            new("axis0.motor.config.calibration_current", CalibrationCurrent),
            new("axis0.motor.config.resistance_calib_max_voltage", ResistanceCalibMaxVoltage),
            new("axis0.motor.config.pre_calibrated", IsMotorPreCalibrated)
        };

        var results = await _session.WriteAsync(writes, CancellationToken.None);
        var failed = results.Where(r => !r.Success).ToArray();
        if (failed.Length == 0)
        {
            LastCommandResult = "电机参数全部写入成功";
            await ReadMotorParametersAsync(); // Refresh
        }
        else
        {
            LastCommandResult = $"电机参数写入部分失败：{string.Join(", ", failed.Select(f => $"{f.Path}:{f.Error}"))}";
        }
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
        var writes = AvailableAxes
            .Select(axis => new PropertyWrite($"axis{axis}.requested_state", AxisStateIdle))
            .ToArray();
        await WriteManyAsync(writes, "保存前切换所有轴到 Idle");
        await InvokeCommandAsync("save_configuration", "保存配置至 ODrive 闪存");
    }
    [RelayCommand(CanExecute = nameof(CanRunDeviceOperation))]
    private async Task RefreshDevicesAsync()
    {
        if (IsDeviceBusy)
        {
            return;
        }

        IsDeviceBusy = true;
        try
        {
            if (_session is not null)
            {
                ConnectionStatus = "正在断开当前设备...";
                _telemetryCts?.Cancel();

                var session = _session;
                _session = null;
                _telemetryCts = null;
                try
                {
                    await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (TimeoutException)
                {
                    ConnectionStatus = "断开设备超时，请拔插设备后重试";
                    return;
                }
                catch (Exception ex)
                {
                    ConnectionStatus = $"断开设备失败：{ex.Message}";
                    return;
                }
            }

            Devices.Clear();
            ConnectionStatus = "正在后台扫描真实 ODrive 设备...";

            var discovered = await Task.Run(async () =>
            {
                var results = new List<DeviceListItemViewModel>();
                foreach (var driver in _drivers)
                {
                    try
                    {
                        var devices = await driver.DiscoverAsync(CancellationToken.None).ConfigureAwait(false);
                        results.AddRange(devices.Select(device => new DeviceListItemViewModel(device, driver)));
                    }
                    catch (Exception ex)
                    {
                        results.Add(new DeviceListItemViewModel(
                            new DeviceInfo($"error:{driver.Name}", $"{driver.Name} 扫描失败：{ex.Message}", string.Empty, string.Empty, driver.Name, true),
                            driver));
                    }
                }

                return results;
            });

            foreach (var item in discovered.Where(item => !item.SerialNumber.StartsWith("error:", StringComparison.OrdinalIgnoreCase)))
            {
                Devices.Add(item);
            }

            SelectedDevice = Devices.FirstOrDefault();
            ConnectionStatus = Devices.Count == 0 ? "未发现设备" : $"发现 {Devices.Count} 个设备，请点击“连接设备”";
        }
        finally
        {
            IsDeviceBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnectSelectedDevice))]
    private async Task ConnectSelectedDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            ConnectionStatus = "请选择设备";
            return;
        }

        if (_session is not null)
        {
            ConnectionStatus = $"已连接：{_session.Info.DisplayName}";
            return;
        }

        IsDeviceBusy = true;
        var selected = SelectedDevice;
        ConnectionStatus = $"正在后台连接 {selected.SerialNumber}...";

        try
        {
            var connected = await Task.Run(async () =>
            {
                var session = await selected.Driver.ConnectAsync(selected.SerialNumber, CancellationToken.None).ConfigureAwait(false);
                var capabilities = await session.GetCapabilitiesAsync(CancellationToken.None).ConfigureAwait(false);
                var schema = await session.GetApiSchemaAsync(CancellationToken.None).ConfigureAwait(false);
                return (session, capabilities, schema);
            });

            _session = connected.session;

            Capabilities.Clear();
            Capabilities.Add($"自定义固件扩展：{ToYesNo(connected.capabilities.SupportsCustomFirmwareExtensions)}");
            Capabilities.Add($"官方 ODrive 兼容层：{ToYesNo(connected.capabilities.SupportsOfficialOdriveCompatibility)}");
            Capabilities.Add($"多摩川编码器：{ToYesNo(connected.capabilities.SupportsTamagawaEncoder)}");
            Capabilities.Add($"实时遥测：{ToYesNo(connected.capabilities.SupportsLiveTelemetry)}");
            Capabilities.Add($"保存配置：{ToYesNo(connected.capabilities.SupportsConfigurationSave)}");
            Capabilities.Add($"编码器类型：{string.Join(", ", connected.capabilities.EncoderTypes)}");
            Capabilities.Add($"特性标记：{string.Join(", ", connected.capabilities.CustomFeatureFlags)}");

            _schema = connected.schema;
            SchemaName = $"{_schema.DisplayName} / schema v{_schema.Version}";
            BuildParameterTree(_schema);

            ConnectionStatus = $"已连接：{_session.Info.DisplayName}";
StartTelemetry(_session, connected.capabilities);
            _ = ReadMotorParametersAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsDeviceBusy = false;
        }
    }

    private bool CanRunDeviceOperation() => !IsDeviceBusy;

    private bool CanConnectSelectedDevice() => !IsDeviceBusy && _session is null && SelectedDevice is not null;

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
    private async Task ReadNodeAsync(ParameterNodeViewModel? node)
    {
        if (node is null || _session is null)
        {
            return;
        }

        await ReadParameterAsync(node);
    }

    [RelayCommand]
    private async Task WriteNodeAsync(ParameterNodeViewModel? node)
    {
        if (node is null || _session is null || node.Property is null)
        {
            return;
        }

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

        var properties = FilterProperties(schema.Properties).ToArray();
        foreach (var property in properties.OrderBy(property => property.Path, StringComparer.OrdinalIgnoreCase))
        {
            AddPropertyNode(property);
        }

        var filterText = string.IsNullOrWhiteSpace(ParameterSearchText) && !ShowWritableOnly
            ? string.Empty
            : $"，当前显示 {properties.Length} 个";
        ParameterStatus = $"已加载 {schema.Properties.Count} 个参数{filterText}";
    }

    private void RebuildParameterTreeFromCurrentSchema()
    {
        if (_schema is null)
        {
            return;
        }

        BuildParameterTree(_schema);
    }

    private IEnumerable<ApiProperty> FilterProperties(IEnumerable<ApiProperty> properties)
    {
        var search = ParameterSearchText.Trim();
        foreach (var property in properties)
        {
            if (ShowWritableOnly && !property.IsWritable)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(search) &&
                !property.Path.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !property.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !(property.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
            {
                continue;
            }

            yield return property;
        }
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

    private void StartTelemetry(IDeviceSession session, DeviceCapabilities capabilities)
    {
        var telemetryCts = new CancellationTokenSource();
        _telemetryCts = telemetryCts;
        var telemetryPaths = new List<string> { "vbus_voltage", "ibus" };
        foreach (var axis in AvailableAxes)
        {
            telemetryPaths.AddRange([
                $"axis{axis}.current_state",
                $"axis{axis}.error",
                $"axis{axis}.motor.error",
                $"axis{axis}.encoder.error",
                $"axis{axis}.controller.error",
                $"axis{axis}.encoder.pos_estimate",
                $"axis{axis}.encoder.vel_estimate",
                $"axis{axis}.encoder.is_ready",
                $"axis{axis}.encoder.index_found",
                $"axis{axis}.encoder.shadow_count",
                $"axis{axis}.encoder.count_in_cpr",
                $"axis{axis}.motor.is_armed",
                $"axis{axis}.motor.is_calibrated",
                $"axis{axis}.motor.current_control.iq_measured",
                $"axis{axis}.motor.current_control.iq_setpoint",
                $"axis{axis}.motor.motor_thermistor.temperature",
                $"axis{axis}.motor.fet_thermistor.temperature"
            ]);

            if (capabilities.SupportsTamagawaEncoder)
            {
                telemetryPaths.AddRange([
                    $"axis{axis}.tamagawa.crc_error_count",
                    $"axis{axis}.tamagawa.absolute_position",
                    $"axis{axis}.tamagawa.multi_turn_count",
                    $"axis{axis}.tamagawa.warning_status",
                    $"axis{axis}.tamagawa.error_status"
                ]);
            }
        }

        var subscription = new TelemetrySubscription(telemetryPaths, TimeSpan.FromMilliseconds(250));

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in session.SubscribeTelemetryAsync(subscription, telemetryCts.Token))
                {
                    Dispatcher.UIThread.Post(() => UpdateTelemetry(frame));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when reconnecting or closing the app.
            }
            catch (Exception) when (telemetryCts.IsCancellationRequested)
            {
                // Closing the serial port interrupts an in-flight read.
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => ConnectionStatus = $"遥测已停止：{ex.Message}");
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
        if (path == "vbus_voltage")
        {
            VbusVoltageText = $"{ToDouble(value):0.00} V";
            return;
        }

        if (path == "ibus")
        {
            IbusText = $"{ToDouble(value):0.00} A";
            return;
        }

        var selectedAxisPrefix = $"axis{SelectedAxis}.";
        if (!path.StartsWith(selectedAxisPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var suffix = path[selectedAxisPrefix.Length..];
        switch (suffix)
        {
            case "current_state":
                AxisStateText = FormatAxisState(GetLong(value));
                ControllerLoopStatus = AxisStateText;
                break;
            case "error":
                AxisErrorText = FormatErrorWord(GetLong(value));
                break;
            case "motor.error":
                MotorErrorText = FormatErrorWord(GetLong(value));
                break;
            case "encoder.error":
                EncoderErrorText = FormatErrorWord(GetLong(value));
                break;
            case "controller.error":
                ControllerErrorText = FormatErrorWord(GetLong(value));
                break;
            case "motor.is_armed":
                MotorArmedText = FormatBooleanState(value, "已上电", "未上电");
                break;
            case "motor.is_calibrated":
                MotorCalibratedText = FormatBooleanState(value, "已标定", "未标定");
                break;
            case "motor.current_control.iq_measured":
                IqMeasuredText = $"{ToDouble(value):0.00} A";
                break;
            case "motor.current_control.iq_setpoint":
                IqSetpointText = $"{ToDouble(value):0.00} A";
                break;
            case "motor.motor_thermistor.temperature":
                MotorTemperatureText = FormatTemperature(value);
                break;
            case "motor.fet_thermistor.temperature":
                FetTemperatureText = FormatTemperature(value);
                break;
            case "encoder.pos_estimate":
                EncoderPositionText = $"{ToDouble(value):0.000} Turns";
                break;
            case "encoder.vel_estimate":
                EncoderVelocityRpmText = $"{ToDouble(value) * 60.0:0.0} RPM";
                break;
            case "encoder.is_ready":
                EncoderReadyText = FormatBooleanState(value, "Ready", "Not Ready");
                break;
            case "encoder.index_found":
                EncoderIndexFoundText = FormatBooleanState(value, "Found", "Not Found");
                break;
            case "encoder.shadow_count":
                EncoderShadowCountText = FormatTelemetryValue(value);
                break;
            case "encoder.count_in_cpr":
                EncoderCountInCprText = FormatTelemetryValue(value);
                break;
            case "tamagawa.crc_error_count":
                TamagawaCrcErrorCountText = FormatTelemetryValue(value);
                break;
            case "tamagawa.absolute_position":
                TamagawaAbsolutePositionText = $"{GetLong(value):N0} / 131,072";
                break;
            case "tamagawa.multi_turn_count":
                TamagawaMultiTurnText = $"{GetLong(value)} 圈";
                break;
            case "tamagawa.warning_status":
                EncoderWarning = GetLong(value) == 0 ? "无报警" : $"报警字 0x{GetLong(value):X2}";
                break;
            case "tamagawa.error_status":
                EncoderError = GetLong(value) == 0 ? "无错误" : $"错误字 0x{GetLong(value):X2}";
                break;
        }
    }

    private string AxisPath(string suffix) => $"axis{SelectedAxis}.{suffix}";

    private async Task WriteAxisStateAsync(int state, string displayName)
    {
        await WriteManyAsync([new PropertyWrite(AxisPath("requested_state"), state)], displayName);
    }

    private async Task RequestWizardAxisStateAsync(int axis, int state, string displayName)
    {
        await WriteManyAsync([new PropertyWrite($"axis{axis}.requested_state", state)], displayName);
    }

    private async Task<PropertyReadResult> ReadWizardValuesAsync(IReadOnlyList<string> paths)
    {
        if (_session is null)
        {
            return new PropertyReadResult(
                new Dictionary<string, object?>(),
                paths.ToDictionary(path => path, _ => "设备未连接", StringComparer.OrdinalIgnoreCase));
        }

        return await _session.ReadAsync(paths, CancellationToken.None);
    }

    private async Task ApplyWizardWritesAsync(IReadOnlyList<PropertyWrite> writes)
    {
        await WriteManyAsync(writes, "写入向导配置");
    }

    private async Task WriteSingleAsync(string path, object? value)
    {
        if (_session is null)
        {
            return;
        }

        await WriteManyAsync([new PropertyWrite(path, value)], $"写入 {path}", reportSuccess: false);
    }

    private async Task WriteManyAsync(IReadOnlyList<PropertyWrite> writes, string displayName, bool reportSuccess = true)
    {
        if (_session is null)
        {
            LastCommandResult = "设备未连接";
            return;
        }

        if (reportSuccess)
        {
            LastCommandResult = $"{displayName}...";
        }

        var results = await _session.WriteAsync(writes, CancellationToken.None);
        var failed = results.FirstOrDefault(result => !result.Success);
        if (failed is not null)
        {
            LastCommandResult = $"{displayName}失败：{failed.Path} {failed.Error}";
            return;
        }

        if (reportSuccess)
        {
            LastCommandResult = $"{displayName}完成";
        }
    }

    private async Task RefreshSelectedAxisStatusAsync()
    {
        if (_session is null)
        {
            return;
        }

        var paths = new[]
        {
            AxisPath("current_state"),
            AxisPath("error"),
            AxisPath("motor.error"),
            AxisPath("encoder.error"),
            AxisPath("controller.error")
        };

        var result = await _session.ReadAsync(paths, CancellationToken.None);
        AxisStateText = FormatAxisState(GetLong(result.Values, paths[0]));
        AxisErrorText = FormatErrorWord(GetLong(result.Values, paths[1]));
        MotorErrorText = FormatErrorWord(GetLong(result.Values, paths[2]));
        EncoderErrorText = FormatErrorWord(GetLong(result.Values, paths[3]));
        ControllerErrorText = FormatErrorWord(GetLong(result.Values, paths[4]));
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

    private static string FormatTemperature(object? value)
    {
        var temperature = ToDouble(value);
        return temperature == 0.0 && value is null ? "—" : $"{temperature:0.0} °C";
    }

    private static string FormatBooleanState(object? value, string trueText, string falseText) => value switch
    {
        bool b => b ? trueText : falseText,
        int i => i != 0 ? trueText : falseText,
        long l => l != 0 ? trueText : falseText,
        double d => Math.Abs(d) > double.Epsilon ? trueText : falseText,
        string s when bool.TryParse(s, out var b) => b ? trueText : falseText,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l != 0 ? trueText : falseText,
        null => "—",
        _ => FormatTelemetryValue(value)
    };

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => 0.0
    };

    private static long GetLong(IReadOnlyDictionary<string, object?> values, string path) =>
        values.TryGetValue(path, out var value) ? GetLong(value) : 0;

    private static long GetLong(object? value) => value switch
    {
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        double d => Convert.ToInt64(d),
        float f => Convert.ToInt64(f),
        decimal m => Convert.ToInt64(m),
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => Convert.ToInt64(d),
        _ => 0
    };

    private static string FormatAxisState(long state) => state switch
    {
        1 => "IDLE",
        2 => "STARTUP_SEQUENCE",
        3 => "FULL_CALIBRATION_SEQUENCE",
        4 => "MOTOR_CALIBRATION",
        5 => "SENSORLESS_CONTROL",
        6 => "ENCODER_INDEX_SEARCH",
        7 => "ENCODER_OFFSET_CALIBRATION",
        8 => "CLOSED_LOOP_CONTROL",
        10 => "ENCODER_DIR_FIND",
        11 => "HOMING",
        12 => "ENCODER_HALL_POLARITY_CALIBRATION",
        13 => "ENCODER_HALL_PHASE_CALIBRATION",
        14 => "ANTICOGGING_CALIBRATION",
        _ => state == 0 ? "UNDEFINED" : $"STATE {state}"
    };

    private static string FormatErrorWord(long error) => error == 0 ? "none" : $"0x{error:X}";

    private static string ToYesNo(bool value) => value ? "支持" : "不支持";
}
