using CommunityToolkit.Mvvm.ComponentModel;

namespace OdriveUpper.App.ViewModels;

public sealed partial class TelemetryValueViewModel : ObservableObject
{
    public TelemetryValueViewModel(string path, string value)
    {
        Path = path;
        Value = value;
    }

    public string Path { get; }

    [ObservableProperty]
    private string _value;
}
