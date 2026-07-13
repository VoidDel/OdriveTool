using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OdriveUpper.Core.Firmware;

namespace OdriveUpper.App.ViewModels;

public sealed partial class ParameterNodeViewModel : ObservableObject
{
    public ParameterNodeViewModel(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }

    public string Path { get; }

    public ObservableCollection<ParameterNodeViewModel> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public ApiProperty? Property { get; private set; }

    public bool IsParameter => Property is not null;

    public string ValueType => Property?.ValueType ?? "object";

    public string Unit => Property?.Unit ?? string.Empty;

    public bool IsWritable => Property?.IsWritable == true;

    public string Description => Property?.Description ?? string.Empty;

    [ObservableProperty]
    private string _currentValue = "—";

    [ObservableProperty]
    private string _pendingValue = string.Empty;

    [ObservableProperty]
    private string _status = "未读取";

    [ObservableProperty]
    private bool _isSelected;

    public void AttachProperty(ApiProperty property)
    {
        Property = property;
        OnPropertyChanged(nameof(IsParameter));
        OnPropertyChanged(nameof(ValueType));
        OnPropertyChanged(nameof(Unit));
        OnPropertyChanged(nameof(IsWritable));
        OnPropertyChanged(nameof(Description));
    }
}
