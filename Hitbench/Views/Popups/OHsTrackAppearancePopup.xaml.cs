using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.Services.Ui;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Views.Popups;

public partial class OHsTrackAppearancePopup : BasePopup
{
    private readonly OHsTrackAppearancePopupVM viewModel;
    private readonly Action<bool> complete;
    private readonly Func<Window, Size> getWorkArea;
    private bool updatingSize;
    private bool watchingWorkArea;

    public OHsTrackAppearancePopup(OHsTrackAppearancePopupVM viewModel) : this(viewModel, null) { }

    internal OHsTrackAppearancePopup(OHsTrackAppearancePopupVM viewModel, Action<bool>? complete,
        Func<Window, Size>? getWorkArea = null)
    {
        this.viewModel = viewModel;
        this.complete = complete ?? (accepted => DialogResult = accepted);
        this.getWorkArea = getWorkArea ?? WindowWorkArea.GetClientSize;
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => RefreshWorkArea();
        Loaded += OnPopupLoaded;
        SizeChanged += (_, _) => { if (IsLoaded) RefreshWorkArea(); };
        LocationChanged += (_, _) => { if (IsLoaded) RefreshWorkArea(); };
        DpiChanged += (_, _) => RefreshWorkArea();
        Closed += (_, _) =>
        {
            if (watchingWorkArea) SystemParameters.StaticPropertyChanged -= OnWorkAreaChanged;
            watchingWorkArea = false;
        };
    }

    protected override bool ShouldBubbleApplicationClose => false;

    private void OnPopupLoaded(object sender, RoutedEventArgs e)
    {
        if (!watchingWorkArea)
        {
            SystemParameters.StaticPropertyChanged += OnWorkAreaChanged;
            watchingWorkArea = true;
        }
        RefreshWorkArea();
        ColorInput.Focus();
    }

    private void OnWorkAreaChanged(object? sender, PropertyChangedEventArgs e) => RefreshWorkArea();

    private void RefreshWorkArea()
    {
        if (updatingSize) return;
        updatingSize = true;
        try { ApplyWorkArea(getWorkArea(this), Math.Max(0, ActualHeight - AppearanceScroll.ActualHeight)); }
        finally { updatingSize = false; }
    }

    internal void ApplyWorkArea(Size workArea, double chromeHeight = 0)
    {
        if (workArea.IsEmpty || !double.IsFinite(workArea.Width) || !double.IsFinite(workArea.Height) ||
            workArea.Width <= 0 || workArea.Height <= 0 || !double.IsFinite(chromeHeight) || chromeHeight < 0) return;
        MinWidth = Math.Min(320, workArea.Width);
        MaxWidth = workArea.Width;
        Width = Math.Min(420, workArea.Width);
        MaxHeight = workArea.Height;
        AppearanceScroll.MaxHeight = Math.Max(0, workArea.Height - chromeHeight);
    }

    internal bool HandleKey(Key key, DependencyObject? originalSource = null)
    {
        if (key == Key.Escape)
        {
            if (!viewModel.IsDirty) complete(false);
            return true;
        }
        if (key == Key.Enter)
        {
            if (DependencyObjectTree.IsDescendantOf(originalSource, CancelAction)) complete(false);
            else if (viewModel.CanSave) complete(true);
            return true;
        }
        return false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && HandleKey(e.Key, e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnCloseButtonClick() { if (!viewModel.IsDirty) complete(false); }
    private void OnCancel(object sender, RoutedEventArgs e) => complete(false);
    private void OnSave(object sender, RoutedEventArgs e) { if (viewModel.CanSave) complete(true); }
}
