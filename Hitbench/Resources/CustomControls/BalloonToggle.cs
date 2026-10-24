using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hitbench.Resources.CustomControls;

[TemplatePart(Name = PartStatePopup, Type = typeof(Popup))]
[TemplatePart(Name = PartStateItems, Type = typeof(ItemsControl))]
public class BalloonToggle : BalloonControl
{
    private const string PartStatePopup = "PART_StatePopup";
    private const string PartStateItems = "PART_StateItems";
    internal static readonly TimeSpan LongPressDuration = TimeSpan.FromMilliseconds(500);

    private static readonly DependencyPropertyKey IsCyclingPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsCycling), typeof(bool), typeof(BalloonToggle),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsCyclingProperty = IsCyclingPropertyKey.DependencyProperty;

    public static readonly DependencyProperty StatesProperty =
        DependencyProperty.Register(nameof(States), typeof(FreezableCollection<BalloonToggleState>),
            typeof(BalloonToggle));

    private BalloonToggleState? _activeState;
    private BalloonToggleState[] _stateOrder = [];
    private readonly DispatcherTimer _longPressTimer;
    private Popup? _statePopup;
    private ItemsControl? _stateItems;
    private UIElement? _statePopupChild;
    private bool _suppressCycling;

    static BalloonToggle()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonToggle),
            new FrameworkPropertyMetadata(typeof(BalloonToggle)));
    }

    public BalloonToggle()
    {
        SetValue(StatesProperty, new FreezableCollection<BalloonToggleState>());
        States.Changed += OnStatesChanged;
        _longPressTimer = new DispatcherTimer { Interval = LongPressDuration };
        _longPressTimer.Tick += OnLongPressTimerTick;
        IsEnabledChanged += OnIsEnabledChanged;
    }

    public bool IsCycling => (bool)GetValue(IsCyclingProperty);

    public FreezableCollection<BalloonToggleState> States =>
        (FreezableCollection<BalloonToggleState>)GetValue(StatesProperty);

    public override void OnApplyTemplate()
    {
        DetachPopup();
        base.OnApplyTemplate();
        _statePopup = GetTemplateChild(PartStatePopup) as Popup;
        _stateItems = GetTemplateChild(PartStateItems) as ItemsControl;
        if (_statePopup is null)
            return;

        _statePopup.Closed += OnStatePopupClosed;
        _statePopupChild = _statePopup.Child;
        if (_statePopupChild is null)
            return;

        _statePopupChild.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnPopupButtonClick));
        _statePopupChild.MouseLeave += OnStatePopupMouseLeave;
        _statePopupChild.PreviewKeyDown += OnPopupPreviewKeyDown;
    }

    protected override void OnClick()
    {
        if (_suppressCycling || _statePopup?.IsOpen == true)
            return;

        Cycle();
        base.OnClick();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (IsEnabled && _statePopup?.IsOpen != true)
            _longPressTimer.Start();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _longPressTimer.Stop();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        _longPressTimer.Stop();
    }

    private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue)
            _longPressTimer.Stop();
    }

    internal BalloonToggleState[] GetPopupStates() => _activeState is null
        ? [.. States]
        : [.. States.Where(state => !ReferenceEquals(state, _activeState))];

    internal bool TryOpenStatePopup()
    {
        if (States.Count == 0)
            return false;

        _suppressCycling = true;
        if (_stateItems is not null)
            _stateItems.ItemsSource = GetPopupStates();

        if (_statePopup is not null)
        {
            _statePopup.IsOpen = true;
            Dispatcher.BeginInvoke(() => _statePopupChild?.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.First)));
        }

        return true;
    }

    internal void SelectState(BalloonToggleState state)
    {
        if (!States.Contains(state) || !CanActivate(state))
            return;

        Activate(state);
        if (_statePopup is not null)
            _statePopup.IsOpen = false;
        else
            _suppressCycling = false;
    }

    private void Cycle()
    {
        if (States.Count == 0)
            return;

        var index = _activeState is null ? -1 : States.IndexOf(_activeState);
        if (_activeState is not null && index < 0)
        {
            ResetCycle();
            return;
        }

        for (var nextIndex = index + 1; nextIndex < States.Count; nextIndex++)
        {
            if (!CanActivate(States[nextIndex]))
                continue;

            Activate(States[nextIndex]);
            return;
        }

        ResetCycle();
    }

    private static bool CanActivate(BalloonToggleState state) =>
        state.OnChecked?.CanExecute(null) != false;

    private void Activate(BalloonToggleState state)
    {
        _activeState = state;
        SetValue(IsCyclingPropertyKey, true);
        ApplyState();
        if (state.OnChecked?.CanExecute(null) == true)
            state.OnChecked.Execute(null);
    }

    private void ResetCycle()
    {
        _activeState = null;
        SetValue(IsCyclingPropertyKey, false);
        if (States.Count > 0)
            ApplyState();
    }

    protected override Brush ResolveRestingBackground() =>
        SystemParameters.HighContrast ? base.ResolveRestingBackground() : _activeState?.DefaultBackground ?? base.ResolveRestingBackground();

    protected override Brush ResolveHoveredBackground() =>
        SystemParameters.HighContrast ? base.ResolveHoveredBackground() : _activeState?.HoverBackground ?? base.ResolveHoveredBackground();

    protected override Brush ResolvePressedBackground() =>
        SystemParameters.HighContrast ? base.ResolvePressedBackground() : _activeState?.PressedBackground ?? base.ResolvePressedBackground();
    protected override Brush ResolveForeground() =>
        SystemParameters.HighContrast ? base.ResolveForeground() : _activeState?.Foreground ?? base.ResolveForeground();

    protected override object? ResolveButtonIcon() =>
        _activeState?.ButtonIcon ?? base.ResolveButtonIcon();

    protected override string? ResolveButtonText() =>
        _activeState?.ButtonText ?? base.ResolveButtonText();

    private void ApplyState()
    {
        RefreshPresentation();
    }

    private void OnStatesChanged(object? sender, EventArgs e)
    {
        var orderChanged = !_stateOrder.SequenceEqual(States);
        _stateOrder = [.. States];

        if (IsCycling && orderChanged)
        {
            ResetCycle();
            return;
        }

        if (_activeState is not null)
            ApplyState();
        else if (States.Count > 0)
            ApplyState();
    }

    private void OnLongPressTimerTick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        TryOpenStatePopup();
    }

    private void OnPopupButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not BalloonButton { CommandParameter: BalloonToggleState state })
            return;

        SelectState(state);
        e.Handled = true;
    }

    private void OnPopupPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _statePopup is null)
            return;

        _statePopup.IsOpen = false;
        e.Handled = true;
    }

    private void OnStatePopupMouseLeave(object sender, MouseEventArgs e)
    {
        if (_statePopup is not null)
            _statePopup.IsOpen = false;
    }

    private void OnStatePopupClosed(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => _suppressCycling = false);
    }

    private void DetachPopup()
    {
        if (_statePopup is not null)
            _statePopup.Closed -= OnStatePopupClosed;

        if (_statePopupChild is not null)
        {
            _statePopupChild.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnPopupButtonClick));
            _statePopupChild.MouseLeave -= OnStatePopupMouseLeave;
            _statePopupChild.PreviewKeyDown -= OnPopupPreviewKeyDown;
        }

        _statePopup = null;
        _stateItems = null;
        _statePopupChild = null;
    }
}


