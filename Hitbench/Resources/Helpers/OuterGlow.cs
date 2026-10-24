using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Resources.Helpers;

public static class OuterGlow
{
    public static readonly DependencyProperty EffectProperty = DependencyProperty.RegisterAttached(
        "Effect", typeof(DropShadowEffect), typeof(OuterGlow), new PropertyMetadata(null, OnEffectChanged));
    public static readonly DependencyProperty IsHostProperty = DependencyProperty.RegisterAttached(
        "IsHost", typeof(bool), typeof(OuterGlow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(OuterGlow),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, OnActivationChanged));

    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
        "Adorner", typeof(OuterGlowAdorner), typeof(OuterGlow));
    private static readonly DependencyProperty LayoutHandlerProperty = DependencyProperty.RegisterAttached(
        "LayoutHandler", typeof(EventHandler), typeof(OuterGlow));
    private static readonly DependencyProperty IsUnloadedProperty = DependencyProperty.RegisterAttached(
        "IsUnloaded", typeof(bool), typeof(OuterGlow), new PropertyMetadata(false));

    public static DropShadowEffect? GetEffect(DependencyObject element) => (DropShadowEffect?)element.GetValue(EffectProperty);
    public static void SetEffect(DependencyObject element, DropShadowEffect? value) => element.SetValue(EffectProperty, value);
    public static bool GetIsHost(DependencyObject element) => (bool)element.GetValue(IsHostProperty);
    public static void SetIsHost(DependencyObject element, bool value) => element.SetValue(IsHostProperty, value);
    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);

    private static void OnEffectChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement source) return;
        StopTracking(source);
        source.Loaded -= OnLoaded;
        source.Unloaded -= OnUnloaded;
        if (e.NewValue is null)
        {
            if ((bool)source.GetValue(IsUnloadedProperty)) source.Loaded += OnLoaded;
            return;
        }
        source.Loaded += OnLoaded;
        source.Unloaded += OnUnloaded;
        StartTracking(source);
    }

    private static void OnActivationChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement source || GetEffect(source) is null) return;
        if ((bool)e.NewValue) StartTracking(source);
        else StopTracking(source);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var source = (FrameworkElement)sender;
        source.SetValue(IsUnloadedProperty, false);
        if (GetEffect(source) is null) { source.Loaded -= OnLoaded; return; }
        StartTracking(source);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var source = (FrameworkElement)sender;
        source.SetValue(IsUnloadedProperty, true);
        StopTracking(source);
    }

    private static void StartTracking(FrameworkElement source)
    {
        if (!GetIsActive(source) || GetEffect(source) is null || (bool)source.GetValue(IsUnloadedProperty) ||
            PresentationSource.FromVisual(source) is null) return;
        if (source.GetValue(LayoutHandlerProperty) is null)
        {
            // LayoutUpdated has no sender; retain exactly one removable callback per active source.
            EventHandler handler = (_, _) => Refresh(source);
            source.SetValue(LayoutHandlerProperty, handler);
            source.LayoutUpdated += handler;
            source.DataContextChanged += OnDataContextChanged;
            source.IsVisibleChanged += OnStateChanged;
            source.IsEnabledChanged += OnStateChanged;
            source.IsKeyboardFocusWithinChanged += OnStateChanged;
        }
        Refresh(source);
    }

    private static void StopTracking(FrameworkElement source)
    {
        if (source.GetValue(LayoutHandlerProperty) is EventHandler handler)
        {
            source.LayoutUpdated -= handler;
            source.ClearValue(LayoutHandlerProperty);
        }
        source.DataContextChanged -= OnDataContextChanged;
        source.IsVisibleChanged -= OnStateChanged;
        source.IsEnabledChanged -= OnStateChanged;
        source.IsKeyboardFocusWithinChanged -= OnStateChanged;
        RemoveAdorner(source);
    }

    private static void OnStateChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh((FrameworkElement)sender);

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var source = (FrameworkElement)sender;
        RemoveAdorner(source);
        Refresh(source);
    }

    private static void Refresh(FrameworkElement source)
    {
        if (!source.IsVisible || !source.IsEnabled || !GetIsActive(source) ||
            GetEffect(source) is not { } effect || PresentationSource.FromVisual(source) is null)
        {
            RemoveAdorner(source);
            return;
        }

        ScrollContentPresenter? viewport = null;
        AdornerDecorator? host = null;
        for (var parent = VisualTreeHelper.GetParent(source); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            viewport ??= parent as ScrollContentPresenter;
            if (viewport is not null && parent is AdornerDecorator decorator && GetIsHost(decorator))
            {
                host = decorator;
                break;
            }
        }
        if (host is null || viewport is null || !GetIsActive(host))
        {
            RemoveAdorner(source);
            return;
        }

        var adorner = (OuterGlowAdorner?)source.GetValue(AdornerProperty);
        if (adorner is not null && !ReferenceEquals(VisualTreeHelper.GetParent(adorner), host.AdornerLayer))
        {
            RemoveAdorner(source);
            adorner = null;
        }
        var clearance = source.IsKeyboardFocusWithin
            ? (double)source.FindResource("Focus.Gap") + (double)source.FindResource("Focus.Thickness") : 0;
        if (adorner is null)
        {
            adorner = new OuterGlowAdorner(source, viewport, effect);
            if (!adorner.UpdateViewport(clearance)) { adorner.Release(); return; }
            source.SetValue(AdornerProperty, adorner);
            host.AdornerLayer.Add(adorner);
        }
        else if (!adorner.UpdateViewport(clearance)) RemoveAdorner(source);
    }

    private static void RemoveAdorner(FrameworkElement source)
    {
        if (source.GetValue(AdornerProperty) is not OuterGlowAdorner adorner) return;
        source.ClearValue(AdornerProperty);
        (VisualTreeHelper.GetParent(adorner) as AdornerLayer)?.Remove(adorner);
        adorner.Release();
    }
}
