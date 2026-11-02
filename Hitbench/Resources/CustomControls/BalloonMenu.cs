using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Automation.Peers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Hitbench.Resources.Helpers;

namespace Hitbench.Resources.CustomControls;

[ContentProperty(nameof(Options))]
[TemplatePart(Name = "PART_StatePopup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_StateItems", Type = typeof(ItemsControl))]
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
    private ItemsControl? items;
    private FrameworkElement? icon;
    private AdornerDecorator? popupRoot;
    private Grid? popupFrame;
    private BalloonMenuAdorner? adorner;
    private AdornerLayer? presentationLayer;
    private Window? ownerWindow;
    private BalloonMenuOption[] adoptedOptions = [];
    private Rect anchor;
    private double expandedWidth;
    private double outset;
    private bool attached;
    private bool targetOpen;
    private bool keyboardOwned;
    private DispatcherOperation? pendingLeave;

    static BalloonMenu()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonMenu), new FrameworkPropertyMetadata(typeof(BalloonMenu)));
        ShouldShowTextProperty.OverrideMetadata(typeof(BalloonMenu), new FrameworkPropertyMetadata(false, null, (_, _) => false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(BalloonMenu), new UIPropertyMetadata(true, null,
            (d, value) => (bool)value && !((BalloonMenu)d).IsExpanded));
    }

    public ObservableCollection<BalloonMenuOption> Options { get; } = [];
    public bool IsExpanded => (bool)GetValue(IsExpandedProperty);
    public event EventHandler? Expanded;
    public event EventHandler? Collapsed;
    internal FrameworkElement? PresentationRoot => popupFrame;
    internal double RevealProgress => (double)GetValue(RevealProgressProperty);
    internal long PresentationGeneration { get; private set; }
    internal BalloonMenuOption? ActiveOption { get; private set; }
    protected virtual bool OwnsKeyboardFocus => IsKeyboardFocusWithin || surface?.IsKeyboardFocusWithin == true;
    protected virtual bool FocusMenuElement(UIElement target) => target.Focus();

    protected override AutomationPeer OnCreateAutomationPeer() => new BalloonMenuAutomationPeer(this);

    // Optional substitution for isolated behavior tests; production uses the adorner layer.
    internal Action<FrameworkElement, bool>? PresentPresentation { get; set; }

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
        DetachPopup();
        base.OnApplyTemplate();
        popup = GetTemplateChild("PART_StatePopup") as Popup;
        items = GetTemplateChild("PART_StateItems") as ItemsControl;
        icon = GetTemplateChild("PART_IconSlot") as FrameworkElement;
        surface = popup?.Child as Border;
        if (popup is null || items is null || surface is null) return;

        // Adopt the shared parts; only this instance loses the toggle presentation.
        foreach (var property in new[] { Border.PaddingProperty, Border.BackgroundProperty, Border.CornerRadiusProperty })
            surface.ClearValue(property);
        surface.SetResourceReference(StyleProperty, "BalloonMenuPopupSurfaceStyle");
        // Template-authored values outrank style setters. Replace the adopted
        // padding by resource, without changing the shared toggle template.
        surface.SetResourceReference(Border.PaddingProperty, "BalloonMenuPopupInset");
        RefreshPopupBackground();
        items.ClearValue(ItemsControl.ItemsPanelProperty);
        items.ClearValue(ItemsControl.ItemTemplateProperty);
        items.SetResourceReference(StyleProperty, "BalloonMenuItemsStyle");
        items.SetResourceReference(ItemsControl.ItemsPanelProperty, "HorizontalStackItemsPanelTemplate");
        items.SetValue(ItemsControl.ItemTemplateProperty, null);
        BindOwner(surface, DataContextProperty, nameof(DataContext));
        items.FlowDirection = FlowDirection.RightToLeft;
        BindOwner(surface, IsEnabledProperty, nameof(IsEnabled));
        KeyboardNavigationPolicy.BindSuppressionScope(surface, this);

        outset = (double)FindResource("Space.6");
        popup.Child = null;
        popupRoot = new AdornerDecorator { Child = surface, Margin = new Thickness(outset), FlowDirection = FlowDirection.LeftToRight };
        popupFrame = new Grid { FlowDirection = FlowDirection.LeftToRight };
        popupFrame.Children.Add(popupRoot);
        adorner = new BalloonMenuAdorner(this, popupFrame);
        surface.MouseEnter += OnPopupEnter;
        surface.MouseLeave += OnPopupLeave;
        surface.LostKeyboardFocus += OnMenuLostFocus;
        AdoptOptions();
    }

    private void BindOwner(DependencyObject target, DependencyProperty property, string path) =>
        BindingOperations.SetBinding(target, property, new Binding(path) { Source = this, Mode = BindingMode.OneWay });

    private void AdoptOptions()
    {
        if (items is null || !attached) return;
        if (Options.Distinct().Count() != Options.Count || Options.Any(option => option.MenuOwner is { } owner && owner != this))
            throw new InvalidOperationException("A balloon menu option may belong to only one active menu.");
        foreach (var option in Options)
        {
            option.MenuOwner = this;
            // Order options right-to-left without mirroring their icon/content coordinates.
            option.SetCurrentValue(FlowDirectionProperty, FlowDirection.LeftToRight);
        }
        adoptedOptions = [.. Options];
        items.ItemsSource = Options;
        // Materialize the finite controls even while closed, so their commands,
        // owner context and accessibility are ready before first activation.
        items.Measure(new Size(double.PositiveInfinity, ButtonSize));
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
        if (!attached || !IsEnabled || Options.Count == 0 || popup is null || surface is null || items is null) return false;
        var bounds = CaptureLauncherBounds();
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return false;
        this.keyboardOwned |= keyboardOwned;
        AdoptOptions();
        pendingLeave?.Abort();
        pendingLeave = null;
        var wasOpen = IsExpanded;
        targetOpen = true;
        PresentationGeneration++;
        SetExpanded(true);
        // An Expanded handler can deactivate this owner while another workflow takes over.
        if (!IsExpanded || !attached || !IsEnabled) return false;
        try
        {
            RefreshPlacement();
            if (!IsExpanded) return false;
            if (!wasOpen)
            {
                AttachPresentationEvents();
                presentationLayer = AdornerLayer.GetAdornerLayer(this);
                if (PresentPresentation is null && presentationLayer is null) { CloseMenu(); return false; }
                if (presentationLayer is not null && adorner is not null) presentationLayer.Add(adorner);
                PresentPresentation?.Invoke(surface!, true);
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

    protected virtual Rect CaptureLauncherBounds()
    {
        var root = PresentationSource.FromVisual(this)?.RootVisual;
        return root is null ? new Rect(RenderSize) : TransformToVisual(root).TransformBounds(new Rect(RenderSize));
    }

    protected virtual void RefreshPlacement()
    {
        if (!IsExpanded || popup is null || surface is null || items is null) return;
        RefreshPopupBackground();
        anchor = CaptureLauncherBounds();
        if (anchor.IsEmpty || anchor.Width <= 0 || anchor.Height <= 0) { CloseMenu(); return; }
        foreach (var option in Options) option.SetCurrentValue(ButtonSizeProperty, anchor.Height);
        if (Options.Count > 0) Options[0].SetCurrentValue(WidthProperty, anchor.Width);
        items.Measure(new Size(double.PositiveInfinity, anchor.Height));
        expandedWidth = Math.Max(anchor.Width, items.DesiredSize.Width + surface.BorderThickness.Left + surface.BorderThickness.Right);
        surface.CornerRadius = new CornerRadius(CornerRadius);
        surface.BorderThickness = new Thickness(StrokeThickness);
        surface.BorderBrush = StrokeBrush;
        surface.Width = expandedWidth;
        surface.Height = anchor.Height;
        surface.Measure(new Size(expandedWidth, anchor.Height));
        surface.Arrange(new Rect(0, 0, expandedWidth, anchor.Height));
        adorner?.UpdatePresentation(expandedWidth, anchor.Height, outset);
        UpdateReveal();
    }

    internal Rect CalculateRevealBounds(Rect launcherBounds, double finalWidth, double progress)
    {
        var width = launcherBounds.Width + (Math.Max(launcherBounds.Width, finalWidth) - launcherBounds.Width) * Math.Clamp(progress, 0, 1);
        return new Rect(launcherBounds.Right - width, launcherBounds.Y, width, launcherBounds.Height);
    }

    internal void ApplyRevealProgress(double progress, long generation)
    {
        if (generation != PresentationGeneration) return;
        BeginAnimation(RevealProgressProperty, null);
        SetValue(RevealProgressProperty, Math.Clamp(progress, 0, 1));
    }

    internal bool IsOptionRevealed(BalloonMenuOption option)
    {
        if (!IsExpanded || !targetOpen || RevealProgress <= 0 || surface is null || !ReferenceEquals(option.MenuOwner, this)) return false;
        var reveal = CalculateRevealBounds(anchor, expandedWidth, RevealProgress);
        if (!option.IsArrangeValid || option.RenderSize.Width <= 0) return false;
        var bounds = option.TransformToVisual(surface).TransformBounds(new Rect(option.RenderSize));
        var left = expandedWidth - reveal.Width;
        return bounds.Left >= left && bounds.Right <= left + reveal.Width && bounds.Top >= 0 && bounds.Bottom <= reveal.Height;
    }

    private void UpdateReveal()
    {
        if (surface is not null && !anchor.IsEmpty)
        {
            var reveal = CalculateRevealBounds(anchor, expandedWidth, RevealProgress);
            // Clip the surface, not its sibling AdornerLayer, so focus is never clipped.
            surface.Clip = new RectangleGeometry(new Rect(expandedWidth - reveal.Width, 0, reveal.Width, reveal.Height));
            surface.Opacity = 1;
        }
        if (icon is not null) icon.Opacity = IsExpanded ? 0 : 1;
        if (GetTemplateChild("PART_Shape") is UIElement shape) shape.Opacity = IsExpanded ? 0 : 1;
        foreach (var option in adoptedOptions) option.CoerceValue(IsHitTestVisibleProperty);
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
        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = target == 1 ? EasingMode.EaseOut : EasingMode.EaseIn }
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
    private void OnPopupEnter(object sender, MouseEventArgs e) => OpenMenu(false);
    private void OnPopupLeave(object sender, MouseEventArgs e) => QueueLeave();
    protected virtual bool IsPointerWithinMenu() => new Rect(RenderSize).Contains(Mouse.GetPosition(this)) ||
        (surface is not null && surface.IsMouseOver);

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
        if (adorner is not null) presentationLayer?.Remove(adorner);
        presentationLayer = null;
        var wasOpen = IsExpanded;
        SetExpanded(false);
        if (icon is not null) icon.Opacity = 1;
        if (GetTemplateChild("PART_Shape") is UIElement shape) shape.Opacity = 1;
        ActiveOption = null;
        if (wasOpen && popup is not null) PresentPresentation?.Invoke(surface!, false);
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
        if (key is not (Key.Left or Key.Right or Key.Home or Key.End)) return false;
        keyboardOwned = true;
        var eligible = Options.Where(candidate => candidate.IsEnabled).ToArray();
        if (eligible.Length == 0) { FocusOption(null); return true; }
        var index = Array.IndexOf(eligible, option);
        var forward = key == Key.Left;
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

    private void OnMenuLostFocus(object sender, KeyboardFocusChangedEventArgs e) => QueueFocusCheck();
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
            ownerWindow.LocationChanged += OnOwnerWindowMoved;
            ownerWindow.SizeChanged += OnOwnerWindowMoved;
            ownerWindow.Deactivated += OnOwnerDeactivated;
        }
        LayoutUpdated += OnOwnerMoved;
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
        PresentationSource.AddSourceChangedHandler(this, OnSourceChanged);
    }

    private void DetachPresentationEvents()
    {
        if (ownerWindow is not null)
        {
            ownerWindow.LocationChanged -= OnOwnerWindowMoved;
            ownerWindow.SizeChanged -= OnOwnerWindowMoved;
            ownerWindow.Deactivated -= OnOwnerDeactivated;
            ownerWindow = null;
        }
        LayoutUpdated -= OnOwnerMoved;
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
        PresentationSource.RemoveSourceChangedHandler(this, OnSourceChanged);
    }

    private void OnOwnerMoved(object? sender, EventArgs e) => RefreshPlacement();
    private void OnOwnerWindowMoved(object? sender, EventArgs e) { RefreshPlacement(); }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RefreshPlacement();
    }

    private void OnOwnerDeactivated(object? sender, EventArgs e) => CloseMenu();
    private void OnSourceChanged(object sender, SourceChangedEventArgs e) => CloseMenu();
    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            if (Dispatcher.CheckAccess()) RefreshPopupBackground();
            else Dispatcher.BeginInvoke(RefreshPopupBackground);
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

    private void RefreshPopupBackground()
    {
        if (surface is null) return;
        if (SystemParameters.HighContrast) surface.SetResourceReference(Border.BackgroundProperty, SystemColors.WindowBrushKey);
        else surface.Background = HoveredBackground ?? DefaultBackground;
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is MouseButtonEventArgs { ButtonState: MouseButtonState.Pressed } && !IsPointerWithinMenu())
            CloseMenu();
    }

    private void DetachPopup()
    {
        CloseMenu();
        ReleaseOptions();
        if (surface is not null)
        {
            surface.MouseEnter -= OnPopupEnter;
            surface.MouseLeave -= OnPopupLeave;
            surface.LostKeyboardFocus -= OnMenuLostFocus;
            BindingOperations.ClearBinding(surface, DataContextProperty);
            BindingOperations.ClearBinding(surface, IsEnabledProperty);
            KeyboardNavigationPolicy.BindSuppressionScope(surface, null);
        }
        if (items is not null) BindingOperations.ClearBinding(items, FlowDirectionProperty);
        if (popupRoot is not null && popup is not null)
        {
            popupRoot.Child = null;
            popupFrame?.Children.Clear();
            popup.Child = surface;
        }
        popup = null;
        popupRoot = null;
        popupFrame = null;
        adorner = null;
        surface = null;
        items = null;
        icon = null;
    }

    private void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return;
        SetValue(IsExpandedPropertyKey, expanded);
        CoerceValue(IsHitTestVisibleProperty);
        (UIElementAutomationPeer.FromElement(this) as BalloonMenuAutomationPeer)?.NotifyExpandedChanged(!expanded, expanded);
        if (expanded) Expanded?.Invoke(this, EventArgs.Empty);
        else Collapsed?.Invoke(this, EventArgs.Empty);
    }
}
