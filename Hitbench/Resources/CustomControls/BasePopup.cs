using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Hitbench.Resources.CustomControls;

[TemplatePart(Name = "PART_ContentRoot", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_PopupOverlay", Type = typeof(UIElement))]
public class BasePopup : Window, IPopupHost
{
    private const int OverlayAnimDuration = 200; // ms
    private const int PopupAnimDuration = 180; // ms
    private const double RecenterDeltaTolerance = 0.1;

    // --- PopupTitle ---
    public static readonly DependencyProperty PopupTitleProperty =
        DependencyProperty.Register(nameof(PopupTitle), typeof(string), typeof(BasePopup),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PopupTitleContentProperty =
        DependencyProperty.Register(nameof(PopupTitleContent), typeof(object), typeof(BasePopup),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ShowCloseButtonProperty =
        DependencyProperty.Register(nameof(ShowCloseButton), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowHeaderProperty =
        DependencyProperty.Register(nameof(ShowHeader), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    private IPopupHost? _popupHost;
    private FrameworkElement? _contentRoot;
    private UIElement? _popupOverlay;
    private readonly PopupOverlayHandoffState _popupOverlayHandoffState = new();

    private readonly DispatcherTimer _popupOverlayDeferredHideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(OverlayAnimDuration)
    };

    private EventHandler? _popupOverlayDeferredHideTickHandler;
    private bool _isAnimatingClose;
    private bool _isClosingForPopupHandoff;
    private bool _hasRoutedOwnerOverlayHide;
    private bool _isPopupOverlayHandoffPending;
    private Window? _previousApplicationMainWindow;
    private bool _isApplicationMainWindow;
    private readonly IInputElement? _previouslyFocusedElement;

    static BasePopup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BasePopup),
            new FrameworkPropertyMetadata(typeof(BasePopup)));
    }

    public BasePopup()
    {
        _previouslyFocusedElement = Keyboard.FocusedElement;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Opacity = 0;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);

        // Implicit styles in App.Resources don't auto-apply to derived types,
        // so bind the Style explicitly to the BasePopup resource key.
        SetResourceReference(StyleProperty, typeof(BasePopup));

        Loaded += OnLoaded;
        Closed += OnClosed;
        SizeChanged += OnSizeChanged;
    }

    public string PopupTitle
    {
        get => (string)GetValue(PopupTitleProperty);
        set => SetValue(PopupTitleProperty, value);
    }

    public object? PopupTitleContent
    {
        get => GetValue(PopupTitleContentProperty);
        set => SetValue(PopupTitleContentProperty, value);
    }

    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    public bool ShowHeader
    {
        get => (bool)GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        WireButton("PART_CloseButton", _ => OnCloseButtonClick());

        _contentRoot = GetTemplateChild("PART_ContentRoot") as FrameworkElement;
        _popupOverlay = GetTemplateChild("PART_PopupOverlay") as UIElement;
    }

    private void WireButton(string partName, Action<RoutedEventArgs> handler)
    {
        if (GetTemplateChild(partName) is ButtonBase btn)
            btn.Click += (_, e) => handler(e);
    }

    protected virtual void OnCloseButtonClick()
    {
        Close();
    }

    protected void CloseForPopupHandoff()
    {
        _isClosingForPopupHandoff = true;
        Close();
    }

    protected virtual bool ShouldBubbleApplicationClose => true;

    // Keyboard shortcuts

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (TryHandlePopupShortcut(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    protected bool TryHandlePopupShortcut(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Escape) return false;
        OnCloseButtonClick();
        return true;
    }

    // Overlay & blur on owner

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _popupHost = Owner as IPopupHost;
        _popupHost?.ShowPopupOverlay(RequestedOverlayOpacity, RequestedOverlayBlur);
        BecomeApplicationMainWindow();
        Dispatcher.BeginInvoke(DispatcherPriority.Input,
            () => MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));

        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            OnFadeInCompleted();
            return;
        }

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(PopupAnimDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        fadeIn.Completed += (_, _) => OnFadeInCompleted();
        BeginAnimation(OpacityProperty, fadeIn);
    }

    protected virtual void OnFadeInCompleted()
    {
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || _isAnimatingClose)
            return;

        if (!e.WidthChanged && !e.HeightChanged)
            return;

        if (e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0)
            return;

        RecenterAnimated(e.PreviousSize, e.NewSize);
    }

    private void RecenterAnimated(Size previousSize, Size newSize)
    {
        var (targetLeft, targetTop) = TryGetOwnerCenteredPosition() ??
                                      GetCenterPreservingPosition(previousSize, newSize);

        if (double.IsNaN(targetLeft) || double.IsInfinity(targetLeft) ||
            double.IsNaN(targetTop) || double.IsInfinity(targetTop))
            return;

        var leftDelta = Math.Abs(targetLeft - Left);
        var topDelta = Math.Abs(targetTop - Top);

        if (leftDelta < RecenterDeltaTolerance && topDelta < RecenterDeltaTolerance)
            return;

        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = targetLeft;
            Top = targetTop;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(PopupAnimDuration);

        if (leftDelta >= RecenterDeltaTolerance)
        {
            BeginAnimation(LeftProperty, new DoubleAnimation
            {
                To = targetLeft,
                Duration = duration,
                EasingFunction = easing
            });
        }

        if (topDelta >= RecenterDeltaTolerance)
        {
            BeginAnimation(TopProperty, new DoubleAnimation
            {
                To = targetTop,
                Duration = duration,
                EasingFunction = easing
            });
        }
    }

    private (double Left, double Top)? TryGetOwnerCenteredPosition()
    {
        if (Owner is null || !Owner.IsVisible)
            return null;

        var ownerWidth = Owner.ActualWidth > 0 ? Owner.ActualWidth : Owner.Width;
        var ownerHeight = Owner.ActualHeight > 0 ? Owner.ActualHeight : Owner.Height;

        if (double.IsNaN(ownerWidth) || double.IsNaN(ownerHeight) ||
            double.IsInfinity(ownerWidth) || double.IsInfinity(ownerHeight))
            return null;

        var centeredLeft = Owner.Left + ((ownerWidth - ActualWidth) / 2d);
        var centeredTop = Owner.Top + ((ownerHeight - ActualHeight) / 2d);
        return (centeredLeft, centeredTop);
    }

    private (double Left, double Top) GetCenterPreservingPosition(Size previousSize, Size newSize)
    {
        var deltaWidth = newSize.Width - previousSize.Width;
        var deltaHeight = newSize.Height - previousSize.Height;

        var centeredLeft = Left - (deltaWidth / 2d);
        var centeredTop = Top - (deltaHeight / 2d);
        return (centeredLeft, centeredTop);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Let popup-specific Closing handlers run first.
        base.OnClosing(e);

        // Respect cancellation from popup-specific logic (e.g. unsaved-change prompts).
        if (e.Cancel)
        {
            _isClosingForPopupHandoff = false;
            return;
        }

        // Preserve modal dialog results (true/false) by letting WPF finish the close immediately.
        if (DialogResult.HasValue)
        {
            RestoreApplicationMainWindow();
            RouteOwnerPopupOverlayHide();
            return;
        }

        if (_isAnimatingClose)
        {
            RestoreApplicationMainWindow();
            return;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            RestoreApplicationMainWindow();
            RouteOwnerPopupOverlayHide();
            return;
        }

        e.Cancel = true;
        _isAnimatingClose = true;

        RouteOwnerPopupOverlayHide();

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(PopupAnimDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void BecomeApplicationMainWindow()
    {
        if (!ShouldBubbleApplicationClose || Application.Current is not { } application ||
            !application.Dispatcher.CheckAccess() ||
            ReferenceEquals(application.MainWindow, this))
            return;

        _previousApplicationMainWindow = application.MainWindow;
        application.MainWindow = this;
        _isApplicationMainWindow = true;
    }

    private void RestoreApplicationMainWindow()
    {
        if (!_isApplicationMainWindow || Application.Current is not { } application ||
            !application.Dispatcher.CheckAccess() ||
            !ReferenceEquals(application.MainWindow, this))
            return;

        application.MainWindow = _previousApplicationMainWindow;
        _isApplicationMainWindow = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_isAnimatingClose)
            RouteOwnerPopupOverlayHide();

        CancelPendingPopupOverlayDeferredHide();
        _previouslyFocusedElement?.Focus();
    }

    private void RouteOwnerPopupOverlayHide()
    {
        if (_hasRoutedOwnerOverlayHide || _popupHost is null)
            return;

        if (_isClosingForPopupHandoff)
            _popupHost.HidePopupOverlayForHandoff();
        else
            _popupHost.HidePopupOverlay();

        _hasRoutedOwnerOverlayHide = true;
    }

    public void BeginPopupHandoff()
    {
        if (_popupOverlayHandoffState.ActivePopupCount <= 0)
            return;

        _isPopupOverlayHandoffPending = true;
    }

    protected virtual double RequestedOverlayOpacity => 0.5;
    protected virtual bool RequestedOverlayBlur => true;
    private readonly Stack<(double Opacity, bool Blur)> overlayAppearances = new();
    public void ShowPopupOverlay(double opacity, bool useBlur)
    {
        overlayAppearances.Push((opacity, useBlur));
        ShowPopupOverlayCore(opacity, useBlur);
    }
    public void ShowPopupOverlay() => ShowPopupOverlay(0.5, true);
    private void ShowPopupOverlayCore(double opacity, bool useBlur)
    {
        if (_contentRoot is null || _popupOverlay is null)
            return;

        CancelPendingPopupOverlayDeferredHide();

        _popupOverlayHandoffState.OnPopupShown();
        _contentRoot.Effect = useBlur ? new BlurEffect { Radius = 20, RenderingBias = RenderingBias.Performance } : null;
        if (_popupOverlay is System.Windows.Shapes.Shape shape) shape.Fill = useBlur ? (Brush)FindResource("Brush.Overlay") : Brushes.Black;

        _popupOverlay.BeginAnimation(OpacityProperty, null);
        _popupOverlay.Visibility = Visibility.Visible;

        if (!SystemParameters.ClientAreaAnimation)
        {
            _popupOverlay.Opacity = opacity;
            return;
        }

        var fadeIn = new DoubleAnimation(_popupOverlay.Opacity, opacity, TimeSpan.FromMilliseconds(OverlayAnimDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _popupOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    public void HidePopupOverlay()
    {
        if (_isPopupOverlayHandoffPending)
        {
            _isPopupOverlayHandoffPending = false;
            HidePopupOverlayForHandoff();
            return;
        }

        RestoreOverlayAppearance();
        var hostAction = _popupOverlayHandoffState.OnPopupHidden();
        if (hostAction != PopupOverlayHostAction.HideOverlay)
            return;

        HidePopupOverlayCore();
    }

    public void HidePopupOverlayForHandoff()
    {
        RestoreOverlayAppearance();
        var hostAction = _popupOverlayHandoffState.OnPopupHiddenForHandoff(out var deferredHideGeneration);
        if (hostAction == PopupOverlayHostAction.HideOverlay)
        {
            HidePopupOverlayCore();
            return;
        }

        if (hostAction != PopupOverlayHostAction.DeferHide)
            return;

        SchedulePopupOverlayDeferredHide(deferredHideGeneration);
    }

    private void RestoreOverlayAppearance()
    {
        if (overlayAppearances.Count > 0) overlayAppearances.Pop();
        if (overlayAppearances.TryPeek(out var appearance) && _contentRoot is not null && _popupOverlay is not null)
        {
            _contentRoot.Effect = appearance.Blur ? new BlurEffect { Radius = 20 } : null;
            _popupOverlay.BeginAnimation(OpacityProperty, null);
            _popupOverlay.Opacity = appearance.Opacity;
            if (_popupOverlay is System.Windows.Shapes.Shape shape) shape.Fill = appearance.Blur ? (Brush)FindResource("Brush.Overlay") : Brushes.Black;
        }
    }

    private void SchedulePopupOverlayDeferredHide(int deferredHideGeneration)
    {
        CancelPendingPopupOverlayDeferredHide();
        _popupOverlayDeferredHideTickHandler = (_, _) => OnPopupOverlayDeferredHideTimerTick(deferredHideGeneration);
        _popupOverlayDeferredHideTimer.Tick += _popupOverlayDeferredHideTickHandler;
        _popupOverlayDeferredHideTimer.Start();
    }

    private void OnPopupOverlayDeferredHideTimerTick(int deferredHideGeneration)
    {
        CancelPendingPopupOverlayDeferredHide();

        var hostAction = _popupOverlayHandoffState.ResolveDeferredHide(deferredHideGeneration);
        if (hostAction != PopupOverlayHostAction.HideOverlay)
            return;

        HidePopupOverlayCore();
    }

    private void CancelPendingPopupOverlayDeferredHide()
    {
        _popupOverlayDeferredHideTimer.Stop();
        if (_popupOverlayDeferredHideTickHandler is null)
            return;

        _popupOverlayDeferredHideTimer.Tick -= _popupOverlayDeferredHideTickHandler;
        _popupOverlayDeferredHideTickHandler = null;
    }

    private void HidePopupOverlayCore()
    {
        if (_contentRoot is null || _popupOverlay is null)
            return;

        _contentRoot.Effect = null;
        _popupOverlay.BeginAnimation(OpacityProperty, null);

        if (!SystemParameters.ClientAreaAnimation)
        {
            _popupOverlay.Opacity = 0;
            _popupOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var fadeOut = new DoubleAnimation(_popupOverlay.Opacity, 0, TimeSpan.FromMilliseconds(OverlayAnimDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (_popupOverlayHandoffState.ActivePopupCount > 0)
                return;

            _popupOverlay.BeginAnimation(OpacityProperty, null);
            _popupOverlay.Opacity = 0;
            _popupOverlay.Visibility = Visibility.Collapsed;
        };
        _popupOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }
}
