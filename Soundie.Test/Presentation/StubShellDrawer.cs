using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Soundie.Resources.CustomControls;

namespace Soundie.Test.Presentation;

public sealed class StubShellDrawer : UserControl, IOHsShellDrawer
{
    public Button Action { get; } = new() { Content = "Action" };
    public ComboBox Selector { get; } = new() { ItemsSource = new[] { "Normal", "Soft", "Drum" } };
    public StubShellDrawer()
    {
        var panel = new StackPanel(); panel.Children.Add(Action); panel.Children.Add(Selector); Content = panel;
        Selector.Style = (Style)FindResource("OHsComboBoxStyle");
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);
    }
    public bool CanDismiss => true;
    public bool TryPrepareClose() => true;
    public event EventHandler? CloseRequested;
    public void FocusInitialAction() => Action.Focus();
    public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
