using System.Collections.ObjectModel;
using System.ComponentModel;
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
    internal double RevealProgress => (double)GetValue(RevealProgressProperty);
    internal long PresentationGeneration { get; private set; }

    // All native presentation goes through this replaceable boundary, including teardown.
    internal Action<Popup, bool> PresentPopup { get; set; } =
        static (popup, open) => popup.SetCurrentValue(Popup.IsOpenProperty, open);

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
        items.ClearValue(ItemsControl.ItemsPanelProperty);
        items.ClearValue(ItemsControl.ItemTemplateProperty);
        items.SetResourceReference(StyleProperty, "BalloonMenuItemsStyle");
        BindOwner(surface, DataContextProperty, nameof(DataContext));
        BindOwner(items, FlowDirectionProperty, nameof(FlowDirection));
        BindOwner(surface, IsEnabledProperty, nameof(IsEnabled));
        KeyboardNavigationPolicy.BindSuppressionScope(surface, this);

        outset = (double)FindResource("Space.6");
        popup.Child = null;
        popupRoot = new AdornerDecorator { Child = surface, Margin = new Thickness(outset), FlowDirection = FlowDirection.LeftToRight };
        popup.Child = popupRoot;
        popup.SetCurrentValue(FlowDirectionProperty, FlowDirection.LeftToRight);
        popup.SetCurrentValue(Popup.HorizontalOffsetProperty, 0d);
        popup.SetCurrentValue(Popup.VerticalOffsetProperty, 0d);
        popup.SetCurrentValue(Popup.PlacementProperty, PlacementMode.Relative);
        // Pointer ownership is handled across both regions, without native capture.
        popup.SetCurrentValue(Popup.StaysOpenProperty, true);
        popup.Closed += OnPopupClosed;
        surface.MouseEnter += OnPopupEnter;
        surface.MouseLeave += OnPopupLeave;
        AdoptOptions();
    }

    private void BindOwner(DependencyObject target, DependencyProperty property, string path) =>
        BindingOperations.SetBinding(target, property, new Binding(path) { Source = this, Mode = BindingMode.OneWay });

    private void AdoptOptions()
    {
        if (items is null || !attached) return;
        if (Options.Distinct().Count() != Options.Count || Options.Any(option => option.MenuOwner is { } owner && owner != this))
            throw new InvalidOperationException("A balloon menu option may belong to only one active menu.");
        foreach (var option in Options) option.MenuOwner = this;
        adoptedOptions = [.. Options];
        items.ItemsSource = Options;
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
            if (!wasOpen)
            {
                AttachPresentationEvents();
                PresentPopup(popup, true);
            }
            AnimateReveal(1);
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
        anchor = CaptureLauncherBounds();
        if (anchor.IsEmpty || anchor.Width <= 0 || anchor.Height <= 0) return;
        items.Measure(new Size(double.PositiveInfinity, anchor.Height));
        expandedWidth = Math.Max(anchor.Width, items.DesiredSize.Width);
        surface.Width = expandedWidth;
        surface.Height = anchor.Height;
        popup.SetCurrentValue(Popup.PlacementTargetProperty,
            PresentationSource.FromVisual(this)?.RootVisual as UIElement ?? this);
        popup.SetCurrentValue(Popup.HorizontalOffsetProperty, anchor.X - outset);
        popup.SetCurrentValue(Popup.VerticalOffsetProperty, anchor.Y - outset);
        surface.Measure(new Size(expandedWidth, anchor.Height));
        surface.Arrange(new Rect(0, 0, expandedWidth, anchor.Height));
        UpdateReveal();
    }

    internal Rect CalculateRevealBounds(Rect launcherBounds, double finalWidth, double progress) =>
        new(launcherBounds.X, launcherBounds.Y,
            launcherBounds.Width + (Math.Max(launcherBounds.Width, finalWidth) - launcherBounds.Width) * Math.Clamp(progress, 0, 1),
            launcherBounds.Height);

    internal void ApplyRevealProgress(double progress, long generation)
    {
        if (generation != PresentationGeneration) return;
        BeginAnimation(RevealProgressProperty, null);
        SetValue(RevealProgressProperty, Math.Clamp(progress, 0, 1));
    }

    internal bool IsOptionRevealed(BalloonMenuOption option)
    {
        if (!IsExpanded || !targetOpen || surface is null || !ReferenceEquals(option.MenuOwner, this)) return false;
        var reveal = CalculateRevealBounds(anchor, expandedWidth, RevealProgress);
        if (RevealProgress >= 1) return true;
        if (!option.IsArrangeValid || option.RenderSize.Width <= 0) return false;
        var bounds = option.TransformToVisual(surface).TransformBounds(new Rect(option.RenderSize));
        return bounds.Left >= 0 && bounds.Right <= reveal.Width && bounds.Top >= 0 && bounds.Bottom <= reveal.Height;
    }

    private void UpdateReveal()
    {
        if (surface is not null && !anchor.IsEmpty)
        {
            var reveal = CalculateRevealBounds(anchor, expandedWidth, RevealProgress);
            // Clip the surface, not its sibling AdornerLayer, so focus is never clipped.
            surface.Clip = new RectangleGeometry(new Rect(0, 0, reveal.Width, reveal.Height));
            if (items is not null) items.Opacity = RevealProgress;
        }
        if (icon is not null) icon.Opacity = 1 - RevealProgress;
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
            if (!IsExpanded || generation != PresentationGeneration || IsPointerWithinMenu() || keyboardOwned) return;
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
        if (wasOpen && popup is not null) PresentPopup(popup, false);
    }

    private void OnPopupClosed(object? sender, EventArgs e) { if (IsExpanded) CloseMenu(); }
    private void AttachPresentationEvents()
    {
        ownerWindow = Window.GetWindow(this);
        if (ownerWindow is not null)
        {
            ownerWindow.LocationChanged += OnOwnerMoved;
            ownerWindow.SizeChanged += OnOwnerMoved;
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
            ownerWindow.LocationChanged -= OnOwnerMoved;
            ownerWindow.SizeChanged -= OnOwnerMoved;
            ownerWindow.Deactivated -= OnOwnerDeactivated;
            ownerWindow = null;
        }
        LayoutUpdated -= OnOwnerMoved;
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
        PresentationSource.RemoveSourceChangedHandler(this, OnSourceChanged);
    }

    private void OnOwnerMoved(object? sender, EventArgs e) => RefreshPlacement();
    private void OnOwnerDeactivated(object? sender, EventArgs e) => CloseMenu();
    private void OnSourceChanged(object sender, SourceChangedEventArgs e) => CloseMenu();
    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
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

    private void DetachPopup()
    {
        CloseMenu();
        ReleaseOptions();
        if (popup is not null) popup.Closed -= OnPopupClosed;
        if (surface is not null)
        {
            surface.MouseEnter -= OnPopupEnter;
            surface.MouseLeave -= OnPopupLeave;
            BindingOperations.ClearBinding(surface, DataContextProperty);
            BindingOperations.ClearBinding(surface, IsEnabledProperty);
            KeyboardNavigationPolicy.BindSuppressionScope(surface, null);
        }
        if (items is not null) BindingOperations.ClearBinding(items, FlowDirectionProperty);
        if (popupRoot is not null && popup is not null)
        {
            popupRoot.Child = null;
            popup.Child = surface;
        }
        popup = null;
        popupRoot = null;
        surface = null;
        items = null;
        icon = null;
    }

    private void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return;
        SetValue(IsExpandedPropertyKey, expanded);
        CoerceValue(IsHitTestVisibleProperty);
        if (expanded) Expanded?.Invoke(this, EventArgs.Empty);
        else Collapsed?.Invoke(this, EventArgs.Empty);
    }
}
