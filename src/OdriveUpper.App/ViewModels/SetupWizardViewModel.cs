using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdriveUpper.Core.Devices;

namespace OdriveUpper.App.ViewModels;

public sealed partial class SetupWizardViewModel : ObservableObject
{
    private const int AxisStateMotorCalibration = 4;
    private const int AxisStateEncoderOffsetCalibration = 7;

    private const int MotorTypeHighCurrent = 0;
    private const int EncoderModeIncremental = 0;
    private const int EncoderModeHall = 1;
    private const int ControlModeVoltage = 0;
    private const int ControlModeTorque = 1;
    private const int ControlModeVelocity = 2;
    private const int ControlModePosition = 3;

    private readonly Func<IReadOnlyList<string>, Task<PropertyReadResult>> _readAsync;
    private readonly Func<IReadOnlyList<PropertyWrite>, Task> _applyAsync;
    private readonly Func<Task> _saveAsync;
    private readonly Func<int, int, string, Task> _requestAxisStateAsync;

    public SetupWizardViewModel(
        Func<IReadOnlyList<string>, Task<PropertyReadResult>> readAsync,
        Func<IReadOnlyList<PropertyWrite>, Task> applyAsync,
        Func<Task> saveAsync,
        Func<int, int, string, Task> requestAxisStateAsync)
    {
        _readAsync = readAsync;
        _applyAsync = applyAsync;
        _saveAsync = saveAsync;
        _requestAxisStateAsync = requestAxisStateAsync;

        BuildSteps();
        CurrentStep = Steps[0];
        UpdateStepStates();
        RebuildDraft();
    }

    public ObservableCollection<SetupWizardStepViewModel> Steps { get; } = [];

    public ObservableCollection<WizardDraftEntryViewModel> DraftEntries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(IsReviewStep))]
    [NotifyPropertyChangedFor(nameof(IsNotReviewStep))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    private SetupWizardStepViewModel? _currentStep;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _currentStepIndex;

    [ObservableProperty]
    private string _statusText = "按步骤完成硬件选择、参数输入、复核和写入。";

    public string ProgressText => Steps.Count == 0 ? "—" : $"步骤 {CurrentStepIndex + 1} / {Steps.Count}";

    public bool IsReviewStep => CurrentStep?.Kind == SetupWizardStepKind.Review;

    public bool IsNotReviewStep => !IsReviewStep;

    public string NextButtonText => CurrentStepIndex >= Steps.Count - 2 ? "生成复核清单" : "下一步";

    [RelayCommand]
    private void SelectChoice(SetupWizardChoiceViewModel? choice)
    {
        if (CurrentStep is null || choice is null || !CurrentStep.Choices.Contains(choice))
        {
            return;
        }

        foreach (var item in CurrentStep.Choices)
        {
            item.IsSelected = ReferenceEquals(item, choice);
        }

        CurrentStep.SelectedChoice = choice;
        CurrentStep.IsCompleted = true;
        StatusText = $"已选择：{choice.Title}";
        RebuildDraft();
        UpdateStepStates();
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStepIndex <= 0)
        {
            return;
        }

        MoveTo(CurrentStepIndex - 1);
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (CurrentStep is null)
        {
            return;
        }

        if (!ValidateCurrentStep())
        {
            return;
        }

        CurrentStep.IsCompleted = true;
        RebuildDraft();

        var nextIndex = Math.Min(CurrentStepIndex + 1, Steps.Count - 1);
        MoveTo(nextIndex);

        if (IsReviewStep)
        {
            await RefreshCurrentValuesAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshCurrentValuesAsync()
    {
        RebuildDraft();
        if (DraftEntries.Count == 0)
        {
            StatusText = "没有待写入参数。";
            return;
        }

        StatusText = "正在读取当前参数值...";
        var paths = DraftEntries.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = await _readAsync(paths);
        foreach (var entry in DraftEntries)
        {
            if (result.Values.TryGetValue(entry.Path, out var value))
            {
                entry.CurrentValue = FormatValue(value);
                entry.Status = entry.IsChanged ? "将更新" : "不变";
            }
            else
            {
                entry.CurrentValue = "读取失败";
                entry.Status = result.Errors.TryGetValue(entry.Path, out var error) ? error : "读取失败";
            }
        }

        StatusText = "复核清单已更新。";
    }

    [RelayCommand]
    private async Task ApplyDraftAsync()
    {
        RebuildDraft();
        if (DraftEntries.Count == 0)
        {
            StatusText = "没有待写入参数。";
            return;
        }

        StatusText = "正在写入向导配置...";
        await _applyAsync(DraftEntries.Select(entry => new PropertyWrite(entry.Path, entry.NewValue)).ToArray());
        StatusText = "向导配置已写入，建议复核后保存至闪存。";
        await RefreshCurrentValuesAsync();
    }

    [RelayCommand]
    private async Task SaveConfigurationAsync()
    {
        StatusText = "正在打开保存确认窗口...";
        await _saveAsync();
        StatusText = "请在保存确认窗口中确认待保存参数。";
    }

    [RelayCommand]
    private async Task CalibrateMotorAsync()
    {
        if (CurrentStep?.Axis is not { } axis)
        {
            return;
        }

        StatusText = $"正在请求 Axis {axis} 电机标定...";
        await _requestAxisStateAsync(axis, AxisStateMotorCalibration, $"Axis {axis} 电机标定");
        StatusText = $"Axis {axis} 电机标定命令已发送，请观察状态与错误。";
    }

    [RelayCommand]
    private async Task CalibrateEncoderAsync()
    {
        if (CurrentStep?.Axis is not { } axis)
        {
            return;
        }

        StatusText = $"正在请求 Axis {axis} 编码器偏移标定...";
        await _requestAxisStateAsync(axis, AxisStateEncoderOffsetCalibration, $"Axis {axis} 编码器偏移标定");
        StatusText = $"Axis {axis} 编码器标定命令已发送，请观察状态与错误。";
    }

    private void MoveTo(int index)
    {
        CurrentStepIndex = Math.Clamp(index, 0, Steps.Count - 1);
        CurrentStep = Steps[CurrentStepIndex];
        UpdateStepStates();
    }

    private bool ValidateCurrentStep()
    {
        if (CurrentStep is null || CurrentStep.Kind == SetupWizardStepKind.Review)
        {
            return true;
        }

        if (CurrentStep.Choices.Count > 0 && CurrentStep.SelectedChoice is null)
        {
            StatusText = "请先选择一个选项。";
            return false;
        }

        foreach (var input in CurrentStep.Inputs.Where(input => input.IsRequired))
        {
            if (string.IsNullOrWhiteSpace(input.Value))
            {
                StatusText = $"请填写 {input.Label}。";
                return false;
            }
        }

        return true;
    }

    private void UpdateStepStates()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].IsActive = i == CurrentStepIndex;
            Steps[i].StepNumber = i + 1;
            Steps[i].ShowSeparator = i < Steps.Count - 1;
        }

        OnPropertyChanged(nameof(IsReviewStep));
        OnPropertyChanged(nameof(IsNotReviewStep));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void RebuildDraft()
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps.Where(step => step.Kind != SetupWizardStepKind.Review))
        {
            AddStepDraft(step, values);
        }

        DraftEntries.Clear();
        foreach (var (path, value) in values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            DraftEntries.Add(new WizardDraftEntryViewModel(path, value));
        }
    }

    private static void AddStepDraft(SetupWizardStepViewModel step, Dictionary<string, object?> values)
    {
        if (step.SelectedChoice is { } choice)
        {
            foreach (var write in choice.Writes)
            {
                values[write.Path] = write.Value;
            }
        }

        switch (step.Kind)
        {
            case SetupWizardStepKind.Brake:
                if (step.SelectedChoice?.Key == "brake.none")
                {
                    values["config.brake_resistance"] = 0d;
                }
                else if (TryFindInput(step, "brake", out var brake))
                {
                    values["config.brake_resistance"] = ParseInputValue(brake);
                }
                break;

            case SetupWizardStepKind.Motor:
                if (step.Axis is not { } motorAxis)
                {
                    break;
                }

                if (step.SelectedChoice?.Key.EndsWith(".other", StringComparison.Ordinal) == true)
                {
                    if (TryFindInput(step, "pole_pairs", out var polePairs))
                    {
                        values[$"axis{motorAxis}.motor.config.pole_pairs"] = ParseInputValue(polePairs);
                    }

                    if (TryFindInput(step, "kv", out var kvInput) &&
                        double.TryParse(kvInput.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var kv) && kv > 0)
                    {
                        values[$"axis{motorAxis}.motor.config.torque_constant"] = 8.27 / kv;
                    }

                    values[$"axis{motorAxis}.motor.config.motor_type"] = MotorTypeHighCurrent;
                }
                break;

            case SetupWizardStepKind.Encoder:
                if (step.Axis is not { } encoderAxis || step.SelectedChoice is null)
                {
                    break;
                }

                if (step.SelectedChoice.Key.EndsWith(".hall", StringComparison.Ordinal))
                {
                    var polePath = $"axis{encoderAxis}.motor.config.pole_pairs";
                    var polePairs = values.TryGetValue(polePath, out var poleValue) ? Convert.ToDouble(poleValue, CultureInfo.InvariantCulture) : 7d;
                    values[$"axis{encoderAxis}.encoder.config.cpr"] = (long)(6 * polePairs);
                    values[$"axis{encoderAxis}.encoder.config.use_index"] = false;
                    values[$"axis{encoderAxis}.encoder.config.mode"] = EncoderModeHall;
                }
                else if (step.SelectedChoice.Key.EndsWith(".incremental_no_index", StringComparison.Ordinal) ||
                         step.SelectedChoice.Key.EndsWith(".incremental_index", StringComparison.Ordinal))
                {
                    if (TryFindInput(step, "cpr", out var cpr))
                    {
                        values[$"axis{encoderAxis}.encoder.config.cpr"] = ParseInputValue(cpr);
                    }

                    values[$"axis{encoderAxis}.encoder.config.mode"] = EncoderModeIncremental;
                    values[$"axis{encoderAxis}.encoder.config.use_index"] = step.SelectedChoice.Key.EndsWith(".incremental_index", StringComparison.Ordinal);
                }
                break;

            case SetupWizardStepKind.Misc:
                foreach (var input in step.Inputs)
                {
                    if (!string.IsNullOrWhiteSpace(input.Path))
                    {
                        values[input.Path] = ParseInputValue(input);
                    }
                }
                break;
        }
    }

    private static bool TryFindInput(SetupWizardStepViewModel step, string key, out WizardInputViewModel input)
    {
        input = step.Inputs.FirstOrDefault(item => item.Key == key)!;
        return input is not null;
    }

    private static object? ParseInputValue(WizardInputViewModel input)
    {
        var value = input.Value.Trim();
        if (input.ValueType.Contains("bool", StringComparison.OrdinalIgnoreCase))
        {
            return value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        if (input.ValueType.Contains("int", StringComparison.OrdinalIgnoreCase) ||
            input.ValueType.Contains("uint", StringComparison.OrdinalIgnoreCase) ||
            input.ValueType.Contains("enum", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : value;
        }

        if (input.ValueType.Contains("float", StringComparison.OrdinalIgnoreCase) ||
            input.ValueType.Contains("double", StringComparison.OrdinalIgnoreCase))
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : value;
        }

        return value;
    }

    private void BuildSteps()
    {
        Steps.Add(new SetupWizardStepViewModel(SetupWizardStepKind.Odrive, "ODrive 版本", "选择你正在配置的 ODrive v3.6 硬件版本。")
        {
            Choices =
            {
                new("odrive.24v", "ODrive v3.6 24V", "24V 版本；用于记录设置流程，不直接写入参数。"),
                new("odrive.56v", "ODrive v3.6 56V", "56V 版本；用于记录设置流程，不直接写入参数。")
            }
        });

        Steps.Add(new SetupWizardStepViewModel(SetupWizardStepKind.Brake, "制动电阻", "设置 config.brake_resistance。无制动电阻时写入 0。")
        {
            Choices =
            {
                new("brake.enabled", "使用制动电阻", "填写实际制动电阻阻值，单位 Ohm。"),
                new("brake.none", "没有制动电阻", "写入 config.brake_resistance = 0。")
            },
            Inputs =
            {
                new("brake", "制动电阻阻值 (Ohm)", "2.0", "float", "选择使用制动电阻时生效。")
            }
        });

        AddAxisSteps(0);
        AddAxisSteps(1);

        Steps.Add(new SetupWizardStepViewModel(SetupWizardStepKind.Review, "复核并写入", "读取当前值、复核差异，然后写入设备；需要断电保留时再保存至闪存。"));
    }

    private void AddAxisSteps(int axis)
    {
        Steps.Add(new SetupWizardStepViewModel(SetupWizardStepKind.Motor, $"Axis {axis} 电机", "选择电机模板或填写自定义 KV / 极对数。")
        {
            Axis = axis,
            HasMotorCalibration = true,
            Choices =
            {
                new($"axis{axis}.motor.d5065", "ODrive D5065", "7 极对，270KV，高电流电机。", [
                    new($"axis{axis}.motor.config.pole_pairs", 7),
                    new($"axis{axis}.motor.config.torque_constant", 8.27 / 270),
                    new($"axis{axis}.motor.config.motor_type", MotorTypeHighCurrent)]),
                new($"axis{axis}.motor.d6374", "ODrive D6374", "7 极对，150KV，高电流电机。", [
                    new($"axis{axis}.motor.config.pole_pairs", 7),
                    new($"axis{axis}.motor.config.torque_constant", 8.27 / 150),
                    new($"axis{axis}.motor.config.motor_type", MotorTypeHighCurrent)]),
                new($"axis{axis}.motor.other", "其他电机", "填写 KV 与极对数，向导会计算 torque_constant = 8.27 / KV。")
            },
            Inputs =
            {
                new("kv", "自定义电机 KV", "270", "float", "仅选择“其他电机”时生效。"),
                new("pole_pairs", "极对数 pole_pairs", "7", "int", "仅选择“其他电机”时生效。")
            }
        });

        Steps.Add(new SetupWizardStepViewModel(SetupWizardStepKind.Encoder, $"Axis {axis} 编码器", "选择编码器类型；Hall 编码器会按 6 × pole_pairs 计算 CPR。")
        {
            Axis = axis,
            HasEncoderCalibration = true,
            Choices =
            {
                new($"axis{axis}.encoder.amt102v", "CUI AMT102V", "增量编码器，CPR=8192，使用 Index。", [
                    new($"axis{axis}.encoder.config.cpr", 8192),
                    new($"axis{axis}.encoder.config.use_index", true),
                    new($"axis{axis}.encoder.config.mode", EncoderModeIncremental)]),
                new($"axis{axis}.encoder.hall", "Hall Effect", "霍尔编码器；CPR 自动设置为 6 × pole_pairs。"),
                new($"axis{axis}.encoder.incremental_no_index", "Incremental Without Index", "填写 CPR，不使用 Index。"),
                new($"axis{axis}.encoder.incremental_index", "Incremental With Index", "填写 CPR，并启用 Index。")
            },
            Inputs =
            {
                new("cpr", "增量编码器 CPR", "8192", "int", "仅选择 Incremental 类型时生效。")
            }
        });

        Steps.Add(new SetupWizardStepViewModel(SetupWizardStepKind.Control, $"Axis {axis} 控制模式", "选择闭环控制模式。")
        {
            Axis = axis,
            Choices =
            {
                new($"axis{axis}.control.position", "Position Control", "位置控制。", [new($"axis{axis}.controller.config.control_mode", ControlModePosition)]),
                new($"axis{axis}.control.velocity", "Velocity Control", "速度控制。", [new($"axis{axis}.controller.config.control_mode", ControlModeVelocity)]),
                new($"axis{axis}.control.torque", "Torque Control", "力矩控制。", [new($"axis{axis}.controller.config.control_mode", ControlModeTorque)]),
                new($"axis{axis}.control.voltage", "Voltage Control", "电压控制。", [new($"axis{axis}.controller.config.control_mode", ControlModeVoltage)])
            }
        });

        Steps.Add(new SetupWizardStepViewModel(SetupWizardStepKind.Misc, $"Axis {axis} 限幅", "设置速度限制和电流限制；建议先保守设置。")
        {
            Axis = axis,
            IsCompleted = true,
            Inputs =
            {
                new("vel_limit", "速度限制 vel_limit", "10", "float", "写入 axisN.controller.config.vel_limit。") { Path = $"axis{axis}.controller.config.vel_limit" },
                new("current_lim", "电流限制 current_lim", "10", "float", "写入 axisN.motor.config.current_lim。") { Path = $"axis{axis}.motor.config.current_lim" }
            }
        });
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        double number => number.ToString("G6", CultureInfo.InvariantCulture),
        float number => number.ToString("G6", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}

public enum SetupWizardStepKind
{
    Odrive,
    Brake,
    Motor,
    Encoder,
    Control,
    Misc,
    Review
}

public sealed partial class SetupWizardStepViewModel : ObservableObject
{
    public SetupWizardStepViewModel(SetupWizardStepKind kind, string title, string description)
    {
        Kind = kind;
        Title = title;
        Description = description;
    }

    public SetupWizardStepKind Kind { get; }

    public string Title { get; }

    public string Description { get; }

    public int? Axis { get; init; }

    public ObservableCollection<SetupWizardChoiceViewModel> Choices { get; init; } = [];

    public ObservableCollection<WizardInputViewModel> Inputs { get; init; } = [];

    [ObservableProperty]
    private int _stepNumber;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCalibration))]
    private bool _hasMotorCalibration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCalibration))]
    private bool _hasEncoderCalibration;

    [ObservableProperty]
    private SetupWizardChoiceViewModel? _selectedChoice;

    [ObservableProperty]
    private bool _showSeparator = true;

    public bool HasInputs => Inputs.Count > 0;

    public bool HasChoices => Choices.Count > 0;

    public bool HasCalibration => HasMotorCalibration || HasEncoderCalibration;
}

public sealed partial class SetupWizardChoiceViewModel : ObservableObject
{
    public SetupWizardChoiceViewModel(string key, string title, string description, IReadOnlyList<WizardDraftWrite>? writes = null)
    {
        Key = key;
        Title = title;
        Description = description;
        Writes = writes ?? [];
    }

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }

    public IReadOnlyList<WizardDraftWrite> Writes { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class WizardInputViewModel : ObservableObject
{
    public WizardInputViewModel(string key, string label, string value, string valueType, string description)
    {
        Key = key;
        Label = label;
        Value = value;
        ValueType = valueType;
        Description = description;
    }

    public string Key { get; }

    public string Label { get; }

    public string ValueType { get; }

    public string Description { get; }

    public string Path { get; init; } = string.Empty;

    public bool IsRequired { get; init; } = true;

    [ObservableProperty]
    private string _value;
}

public sealed partial class WizardDraftEntryViewModel : ObservableObject
{
    public WizardDraftEntryViewModel(string path, object? newValue)
    {
        Path = path;
        NewValue = newValue;
        NewValueText = FormatValue(newValue);
    }

    public string Path { get; }

    public object? NewValue { get; }

    public string NewValueText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private string _currentValue = "未读取";

    [ObservableProperty]
    private string _status = "待复核";

    public bool IsChanged => !string.Equals(CurrentValue, NewValueText, StringComparison.OrdinalIgnoreCase);

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        double number => number.ToString("G6", CultureInfo.InvariantCulture),
        float number => number.ToString("G6", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}

public sealed record WizardDraftWrite(string Path, object? Value);
