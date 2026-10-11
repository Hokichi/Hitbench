using System.Windows;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Views.Popups;

public partial class NodePastePopup : BasePopup
{
    private readonly NodePastePopupVM vm;
    private readonly Action<bool> complete;
    public NodePasteTiming? Result { get; private set; }
    public NodePastePopup(NodePastePopupVM vm) : this(vm, null) { }
    internal NodePastePopup(NodePastePopupVM vm, Action<bool>? complete)
    {
        this.vm = vm;
        this.complete = complete ?? (accepted => DialogResult = accepted);
        InitializeComponent(); DataContext = vm;
        Loaded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => MarkerChoice.Focus()));
    }
    protected override bool ShouldBubbleApplicationClose => false;
    protected override void OnCloseButtonClick() { Result = null; complete(false); }
    private void OnCancel(object sender, RoutedEventArgs e) => OnCloseButtonClick();
    private void OnPaste(object sender, RoutedEventArgs e) { Result = vm.Timing; complete(true); }
    internal bool HandlePasteKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Enter && modifiers == ModifierKeys.None) { OnPaste(this, new RoutedEventArgs()); return true; }
        if (key == Key.Escape) { OnCloseButtonClick(); return true; }
        return false;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (HandlePasteKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
        else base.OnPreviewKeyDown(e);
    }
}
