using System.Windows;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.Helpers;
using osuHitsounder.Services.Ui;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Views.Popups;

public partial class OHsMappingConflictsPopup : BasePopup
{
    private readonly MappingConflictsPopupVM viewModel;
    private readonly Action<bool> complete;
    private readonly Func<Window, Size> workArea;
    private Window? sizingOwner;
    public IReadOnlyDictionary<string, string>? Result { get; private set; }
    public OHsMappingConflictsPopup(MappingConflictsPopupVM viewModel) : this(viewModel, null, null) { }
    internal OHsMappingConflictsPopup(MappingConflictsPopupVM viewModel, Action<bool>? complete, Func<Window, Size>? workArea)
    {
        InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
        this.complete = complete ?? (accepted => DialogResult = accepted);
        this.workArea = workArea ?? WindowWorkArea.GetClientSize;
        Loaded += (_, _) =>
        {
            sizingOwner = Owner; if (sizingOwner is not null) sizingOwner.SizeChanged += OwnerSizeChanged;
            UpdateSize(); MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        DpiChanged += (_, _) => UpdateSize();
        Closed += (_, _) => { if (sizingOwner is not null) sizingOwner.SizeChanged -= OwnerSizeChanged; };
        UpdateSize();
    }
    protected override bool ShouldBubbleApplicationClose => false;
    private void OwnerSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSize();
    private void UpdateSize()
    {
        var area = workArea(sizingOwner ?? this);
        if (area.IsEmpty || area.Width <= 0 || area.Height <= 0) area = SystemParameters.WorkArea.Size;
        var ownerSize = sizingOwner?.RenderSize ?? area;
        Width = Math.Max(1, Math.Min(800, Math.Min(area.Width, ownerSize.Width) - 32));
        Height = Math.Max(1, Math.Min(560, Math.Min(area.Height, ownerSize.Height) - 32));
    }
    private void OnConfirm(object sender, RoutedEventArgs e)
    { if (!viewModel.CanConfirm) return; Result = viewModel.Confirm(); complete(true); }
    private void OnCancel(object sender, RoutedEventArgs e) => OnCloseButtonClick();
    protected override void OnCloseButtonClick() { Result = null; complete(false); }
    internal bool HandleCompletionKey(Key key)
    {
        if (key == Key.Escape) { OnCloseButtonClick(); return true; }
        if (key == Key.Enter && viewModel.CanConfirm) { OnConfirm(ConfirmButton, new RoutedEventArgs()); return true; }
        return false;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && HandleCompletionKey(e.Key)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
}
