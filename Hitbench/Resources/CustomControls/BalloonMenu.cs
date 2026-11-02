using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Automation.Peers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Hitbench.Resources.Helpers;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Hitbench.Resources.CustomControls;

[ContentProperty(nameof(Options))]
[TemplatePart(Name = "PART_MenuPopup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_MenuSurface", Type = typeof(Border))]
[TemplatePart(Name = "PART_MenuItems", Type = typeof(ItemsControl))]
public class BalloonMenu : BalloonButton
{
    private static readonly DependencyPropertyKey IsExpandedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsExpanded), typeof(bool), typeof(BalloonMenu), new PropertyMetadata(false));
    public static readonly DependencyProperty IsExpandedProperty = IsExpandedPropertyKey.DependencyProperty;
    private static readonly DependencyProperty RevealProgressProperty = DependencyProperty.Register(
        nameof(RevealProgress), typeof(double), typeof(BalloonMenu),
        new PropertyMetadata(0d, (d, _) => ((BalloonMenu)d).UpdateReveal()));

    private Popup? popup;
    private Border? surface;
    private bool popupRequested;
    private ItemsControl? items;
    private FrameworkElement? icon;
    private Window? ownerWindow;
    private BalloonMenuOption[] adoptedOptions = [];
    private bool attached;
    private bool targetOpen;
    private bool keyboardOwned;
    private DispatcherOperation? pendingLeave;

    static BalloonMenu()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonMenu), new FrameworkPropertyMetadata(typeof(BalloonMenu)));
        ShouldShowTextProperty.OverrideMetadata(typeof(BalloonMenu), new FrameworkPropertyMetadata(false, null, (_, _) => false));

    }

    public ObservableCollection<BalloonMenuOption> Options { get; } = [];
    public bool IsExpanded => (bool)GetValue(IsExpandedProperty);
    public event EventHandler? Expanded;
    public event EventHandler? Collapsed;
    internal FrameworkElement? PresentationRoot => popup?.Child as FrameworkElement;
    internal Action<bool>? PresentPopup { get; set; }
    internal double RevealProgress => (double)GetValue(RevealProgressProperty);
    internal long PresentationGeneration { get; private set; }
    internal BalloonMenuOption? ActiveOption { get; private set; }
    protected virtual bool OwnsKeyboardFocus => IsKeyboardFocusWithin || PresentationRoot?.IsKeyboardFocusWithin == true;
    protected virtual bool FocusMenuElement(UIElement target) => target.Focus();

    protected override AutomationPeer OnCreateAutomationPeer() => new BalloonMenuAutomationPeer(this);

    public BalloonMenu()
    {
        Options.CollectionChanged += (_, _) => { CloseMenu(); ReleaseOptions(); };
        Loaded += (_, _) => { attached = true; AdoptOptions(); };
        Unloaded += (_, _) => { attached = false; CloseMenu(); ReleaseOptions(); };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CloseMenu(); };
        DataContextChanged += (_, _) => CloseMenu();
    }

    public override void OnApplyTemplate()
    {
        DetachTemplate();
        base.OnApplyTemplate();
        popup = GetTemplateChild("PART_MenuPopup") as Popup;
        surface = GetTemplateChild("PART_MenuSurface") as Border;
        items = GetTemplateChild("PART_MenuItems") as ItemsControl;
        icon = GetTemplateChild("PART_IconSlot") as FrameworkElement;
        if (popup is not null)
        {
            popup.Closed += OnPopupClosed;
            popup.CustomPopupPlacementCallback = PlacePopup;
        }
        if (PresentationRoot is { } root)
        {
            foreach (var property in new[] { DataContextProperty, IsEnabledProperty, FlowDirectionProperty, LanguageProperty })
                BindingOperations.SetBinding(root, property, new Binding(property.Name) { Source = this, Mode = BindingMode.OneWay });
            KeyboardNavigationPolicy.BindSuppressionScope(root, this);
            root.MouseEnter += OnPopupEnter;
            root.MouseLeave += OnPopupLeave;
            root.LostKeyboardFocus += OnPopupLostFocus;
        }
        AdoptOptions();
        UpdateReveal();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (surface is not null && (e.Property == HoveredBackgroundProperty || e.Property == StrokeBrushProperty ||
            e.Property == StrokeThicknessProperty || e.Property == CornerRadiusProperty || e.Property == ButtonSizeProperty))
            UpdateReveal();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        return new Size(ButtonSize, ButtonSize);
    }

    private CustomPopupPlacement[] PlacePopup(Size popupSize, Size targetSize, Point offset) =>
        // WPF has already transformed the target offset. Custom placement avoids
        // MenuDropAlignment; RTL target coordinates start at the opposite edge.
        [new(new Point(FlowDirection == FlowDirection.RightToLeft ? -popupSize.Width : 0, 0), PopupPrimaryAxis.None)];

    private void RequestPopupState(bool open)
    {
        if (popupRequested == open) return;
        popupRequested = open;
        if (PresentPopup is { } present) present(open);
        else if (popup is not null) popup.SetCurrentValue(Popup.IsOpenProperty, open);
    }

    private void AdoptOptions()
    {
        if (items is null || !attached) return;
        if (Options.Distinct().Count() != Options.Count || Options.Any(option => option.MenuOwner is { } owner && owner != this))
            throw new InvalidOperationException("A balloon menu option may belong to only one active menu.");
        foreach (var option in Options)
        {
            option.MenuOwner = this;
            // Keep icon coordinates unmirrored; the vertical panel preserves collection order.
            option.SetCurrentValue(FlowDirectionProperty, FlowDirection.LeftToRight);
        }
        adoptedOptions = [.. Options];
        items.ItemsSource = Options;
        // Materialize the finite controls even while closed, so their commands,
        // owner context and accessibility are ready before first activation.
        items.Measure(new Size(ButtonSize, double.PositiveInfinity));
    }

    private void ReleaseOptions()
    {
        if (items is not null) items.ItemsSource = null;
        foreach (var option in adoptedOptions)
            if (ReferenceEquals(option.MenuOwner, this)) option.MenuOwner = null;
        adoptedOptions = [];
    }

    internal bool OpenMenu(bool keyboardOwned)
    {
        if (!attached || !IsEnabled || Options.Count == 0 || items is null || popup is null || surface is null) return false;
        this.keyboardOwned |= keyboardOwned;
        AdoptOptions();
        pendingLeave?.Abort();
        pendingLeave = null;
        if (targetOpen && IsExpanded)
        {
            if (keyboardOwned) { ApplyRevealProgress(1, PresentationGeneration); FocusOption(Options.FirstOrDefault(option => option.IsEnabled)); }
            return true;
        }
        var wasOpen = IsExpanded;
        targetOpen = true;
        PresentationGeneration++;
        try
        {
            SetExpanded(true);
            if (!IsExpanded || !attached || !IsEnabled) return false;
            if (!wasOpen)
            {
                AttachPresentationEvents();
                UpdateReveal();
                RequestPopupState(true);
            }
            AnimateReveal(1);
            if (keyboardOwned)
            {
                ApplyRevealProgress(1, PresentationGeneration);
                FocusOption(Options.FirstOrDefault(option => option.IsEnabled));
            }
            return true;
        }
        catch
        {
            CloseMenu();
            throw;
        }
    }

    internal void ApplyRevealProgress(double progress, long generation)
    {
        if (generation != PresentationGeneration) return;
        BeginAnimation(RevealProgressProperty, null);
        SetValue(RevealProgressProperty, Math.Clamp(progress, 0, 1));
    }

    internal bool IsOptionRevealed(BalloonMenuOption option) =>
        attached && IsEnabled && IsExpanded && targetOpen && RevealProgress >= 1 &&
        ReferenceEquals(option.MenuOwner, this) && Options.Contains(option);

    private void UpdateReveal()
    {
        if (icon is not null) icon.Opacity = 1 - Math.Clamp(RevealProgress / .2, 0, 1);
        if (items is not null) items.Opacity = Math.Clamp((RevealProgress - .8) / .2, 0, 1);
        foreach (var option in adoptedOptions)
        {
            option.CoerceValue(IsHitTestVisibleProperty);
            option.CoerceValue(FocusableProperty);
        }
        if (surface is not null)
        {
            var width = ActualWidth > 0 ? ActualWidth : ButtonSize;
            var height = ActualHeight > 0 ? ActualHeight : ButtonSize;
            var t = Math.Clamp((RevealProgress - .2) / .6, 0, 1);
            surface.Width = width;
            surface.Height = height * (1 + (Math.Max(1, Options.Count) - 1) * (1 - Math.Pow(1 - t, 3)));
            surface.Opacity = RevealProgress >= .2 ? 1 : 0;
            surface.Background = SystemParameters.HighContrast ? SystemColors.HighlightBrush : HoveredBackground;
            surface.BorderBrush = StrokeBrush;
            surface.BorderThickness = new Thickness(StrokeThickness);
            surface.CornerRadius = new CornerRadius(CornerRadius);
            foreach (var option in adoptedOptions)
            {
                option.SetCurrentValue(ButtonSizeProperty, height);
                option.SetCurrentValue(WidthProperty, width);
            }
        }
    }

    protected virtual bool ClientAreaAnimationEnabled => SystemParameters.ClientAreaAnimation;

    private void AnimateReveal(double target)
    {
        var current = RevealProgress;
        ApplyRevealProgress(current, PresentationGeneration);
        if (!ClientAreaAnimationEnabled || Math.Abs(target - current) < .001)
        {
            ApplyRevealProgress(target, PresentationGeneration);
            if (target == 0) CloseMenu();
            return;
        }
        var generation = PresentationGeneration;
        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(200 * Math.Abs(target - current)))
        {

        };
        animation.Completed += (_, _) =>
        {
            if (generation != PresentationGeneration) return;
            ApplyRevealProgress(target, generation);
            if (target == 0) CloseMenu();
        };
        BeginAnimation(RevealProgressProperty, animation);
    }

    internal void RefreshMotionSettings()
    {
        if (IsExpanded && !ClientAreaAnimationEnabled)
        {
            ApplyRevealProgress(targetOpen ? 1 : 0, PresentationGeneration);
            if (!targetOpen) CloseMenu();
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); OpenMenu(false); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); QueueLeave(); }
    protected virtual bool IsPointerWithinMenu() => IsMouseOver || PresentationRoot?.IsMouseOver == true;
    private void OnPopupEnter(object sender, MouseEventArgs e) => OpenMenu(false);
    private void OnPopupLeave(object sender, MouseEventArgs e) => QueueLeave();
    private void OnPopupLostFocus(object sender, KeyboardFocusChangedEventArgs e) => QueueFocusCheck();
    private void OnPopupClosed(object? sender, EventArgs e) => NotifyPopupClosed();
    internal void NotifyPopupClosed() { if (IsExpanded) CloseMenu(); }
    private void OnOwnerGeometryChanged(object? sender, EventArgs e) => CloseMenu();
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (IsExpanded) CloseMenu();
    }

    private void QueueLeave()
    {
        var generation = PresentationGeneration;
        pendingLeave?.Abort();
        pendingLeave = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            pendingLeave = null;
            if (!IsExpanded || generation != PresentationGeneration || IsPointerWithinMenu() || keyboardOwned && OwnsKeyboardFocus) return;
            targetOpen = false;
            PresentationGeneration++;
            AnimateReveal(0);
        });
    }

    public void CloseMenu(bool restoreFocus = false)
    {
        PresentationGeneration++;
        pendingLeave?.Abort();
        pendingLeave = null;
        targetOpen = false;
        keyboardOwned = false;
        ApplyRevealProgress(0, PresentationGeneration);
        DetachPresentationEvents();
        var wasOpen = IsExpanded;
        SetExpanded(false);
        if (icon is not null) icon.Opacity = 1;
        if (GetTemplateChild("PART_Shape") is UIElement shape) shape.Opacity = 1;
        ActiveOption = null;
        RequestPopupState(false);
        if (wasOpen && restoreFocus && attached && IsEnabled) FocusMenuElement(this);
    }

    protected override void OnClick() => OpenMenu(true);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space or Key.Down)
        {
            if (!e.IsRepeat) OpenMenu(true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && IsExpanded) { CloseMenu(true); e.Handled = true; }
        else base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) e.Handled = true;
        else base.OnKeyUp(e);
    }

    internal bool HandleOptionKey(BalloonMenuOption option, Key key)
    {
        if (!IsExpanded || !ReferenceEquals(option.MenuOwner, this)) return false;
        if (key == Key.Escape) { CloseMenu(true); return true; }
        if (key is not (Key.Up or Key.Down or Key.Home or Key.End)) return false;
        keyboardOwned = true;
        var eligible = Options.Where(candidate => candidate.IsEnabled).ToArray();
        if (eligible.Length == 0) { FocusOption(null); return true; }
        var index = Array.IndexOf(eligible, option);
        var forward = key == Key.Down;
        var next = key == Key.Home ? 0 : key == Key.End ? eligible.Length - 1 :
            (Math.Max(0, index) + (forward ? 1 : -1) + eligible.Length) % eligible.Length;
        FocusOption(eligible[next]);
        return true;
    }

    private void FocusOption(BalloonMenuOption? option)
    {
        ActiveOption = option;
        var target = (UIElement?)option ?? this;
        if (FocusMenuElement(target)) return;
        var generation = PresentationGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsExpanded && generation == PresentationGeneration && keyboardOwned) FocusMenuElement(target);
        });
    }

    internal bool CanInvokeOption(BalloonMenuOption option) => attached && IsEnabled &&
        Options.Contains(option) && IsOptionRevealed(option) && option.IsEnabled &&
        (option.Command is RoutedCommand routed ? routed.CanExecute(option.CommandParameter, option.CommandTarget ?? option) :
            option.Command?.CanExecute(option.CommandParameter) != false);

    internal void PrepareOptionInvocation(BalloonMenuOption option)
    {
        if (ReferenceEquals(option.MenuOwner, this)) CloseMenu();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        QueueFocusCheck();
    }

    private void QueueFocusCheck()
    {
        var generation = PresentationGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsExpanded && generation == PresentationGeneration && !OwnsKeyboardFocus) CloseMenu();
        });
    }

    private void AttachPresentationEvents()
    {
        ownerWindow = Window.GetWindow(this);
        if (ownerWindow is not null)
        {
            ownerWindow.Deactivated += OnOwnerDeactivated;
            ownerWindow.LocationChanged += OnOwnerGeometryChanged;
            ownerWindow.SizeChanged += OnOwnerGeometryChanged;
        }
        SizeChanged += OnOwnerGeometryChanged;
        IsVisibleChanged += OnOwnerVisibilityChanged;
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
        PresentationSource.AddSourceChangedHandler(this, OnSourceChanged);
    }

    private void DetachPresentationEvents()
    {
        if (ownerWindow is not null)
        {
            ownerWindow.Deactivated -= OnOwnerDeactivated;
            ownerWindow.LocationChanged -= OnOwnerGeometryChanged;
            ownerWindow.SizeChanged -= OnOwnerGeometryChanged;
            ownerWindow = null;
        }
        SizeChanged -= OnOwnerGeometryChanged;
        IsVisibleChanged -= OnOwnerVisibilityChanged;
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
        PresentationSource.RemoveSourceChangedHandler(this, OnSourceChanged);
    }

    private void OnOwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { if (!(bool)e.NewValue) CloseMenu(); }
    private void OnOwnerDeactivated(object? sender, EventArgs e) => CloseMenu();
    private void OnSourceChanged(object sender, SourceChangedEventArgs e) => CloseMenu();
    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            if (Dispatcher.CheckAccess()) UpdateReveal();
            else Dispatcher.BeginInvoke(UpdateReveal);
            return;
        }
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        if (Dispatcher.CheckAccess()) RefreshMotionSettings();
        else
        {
            var generation = PresentationGeneration;
            Dispatcher.BeginInvoke(() => { if (generation == PresentationGeneration) RefreshMotionSettings(); });
        }
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is MouseButtonEventArgs { ButtonState: MouseButtonState.Pressed } && !IsPointerWithinMenu())
            CloseMenu();
    }

    private void DetachTemplate()
    {
        CloseMenu();
        ReleaseOptions();
        if (popup is not null)
        {
            popup.Closed -= OnPopupClosed;
            popup.CustomPopupPlacementCallback = null;
        }
        if (PresentationRoot is { } root)
        {
            root.MouseEnter -= OnPopupEnter;
            root.MouseLeave -= OnPopupLeave;
            root.LostKeyboardFocus -= OnPopupLostFocus;
            foreach (var property in new[] { DataContextProperty, IsEnabledProperty, FlowDirectionProperty, LanguageProperty })
                BindingOperations.ClearBinding(root, property);
            KeyboardNavigationPolicy.BindSuppressionScope(root, null);
        }
        popup = null;
        surface = null;
        items = null;
        icon = null;
    }

    private void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return;
        SetValue(IsExpandedPropertyKey, expanded);
        (UIElementAutomationPeer.FromElement(this) as BalloonMenuAutomationPeer)?.NotifyExpandedChanged(!expanded, expanded);
        if (expanded) Expanded?.Invoke(this, EventArgs.Empty);
        else Collapsed?.Invoke(this, EventArgs.Empty);
    }
}
