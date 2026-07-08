using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OdriveUpper.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || IsInteractiveElement(e.Source as Visual))
        {
            return;
        }

        try
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
        catch
        {
            // BeginMoveDrag can fail if the platform has already handled the pointer.
        }
    }

    private static bool IsInteractiveElement(Visual? element)
    {
        for (var current = element; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or ComboBox or TextBox or ToggleSwitch or Slider or NumericUpDown or TreeView or ListBox)
            {
                return true;
            }
        }

        return false;
    }
}
