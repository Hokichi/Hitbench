using System.ComponentModel;
using System.Windows;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

public sealed class TestBasePopup : BasePopup
{
    public void LoadForTest() => RaiseEvent(new RoutedEventArgs(LoadedEvent, this));
    public bool CloseForTest()
    {
        var args = new CancelEventArgs();
        OnClosing(args);
        return !args.Cancel;
    }
    public void ClosedForTest() => OnClosed(EventArgs.Empty);
}
