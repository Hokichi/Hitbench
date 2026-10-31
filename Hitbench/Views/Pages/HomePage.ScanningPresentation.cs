using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Hitbench.Resources.CustomControls;
using Hitbench.Services.Ui;

namespace Hitbench.Views.Pages;

public partial class HomePage
{
    private readonly OHsThemeService? themeService;
    private bool scanningMotionEnabled;
    private bool scanningHighContrast;
    internal string ScanningResourcePath { get; private set; } = SpinnerResource(false);
    private static string SpinnerResource(bool light) => "pack://application:,,,/Hitbench;component/Resources/" +
        (light ? "LoadingSpinnerL.json" : "LoadingSpinnerD.json");
    private void OnScanningThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OHsThemeService.Theme)) return;
        if (Dispatcher.CheckAccess()) SynchronizeScanningTheme();
        else Dispatcher.BeginInvoke(new Action(SynchronizeScanningTheme));
    }
    private void SynchronizeScanningTheme()
    {
        if (!attached || lifetime.IsCancellationRequested) return;
        var resource = SpinnerResource(themeService?.IsLightTheme == true);
        if (resource == ScanningResourcePath) return;
        ScanningResourcePath = resource;
        scanningAnimationFailed = false;
        ReleaseScanningAnimation();
        RefreshScanningPresentation(scanningMotionEnabled, scanningHighContrast);
    }
    private OHsScanningAnimationView? scanningAnimation;
    private bool scanningAnimationFailed;
    internal bool IsScanningAnimationPlaying => scanningAnimation?.IsPlaying == true;

    internal void LoadScanningAnimation(string resourcePath)
    {
        ReleaseScanningAnimation();
        try
        {
            scanningAnimation = new OHsScanningAnimationView
            {
                AutoPlay = false, RepeatCount = -1,
                Width = 300, Height = 150, IsHitTestVisible = false, Focusable = false
            };
            scanningAnimation.ResourcePath = resourcePath;
            scanningAnimation.PlaybackFailed += OnScanningPlaybackFailed;
            MapsetScanningAnimationContent.Children.Add(scanningAnimation);
            scanningAnimationFailed = false;
        }
        catch (Exception error)
        {
            Trace.TraceError("Mapset scan animation: {0}", error);
            scanningAnimationFailed = true;
            ReleaseScanningAnimation();
        }
    }

    private void OnScanningPlaybackFailed(object? sender, EventArgs e)
    {
        scanningAnimationFailed = true;
        MapsetScanningAnimationHost.Visibility = Visibility.Collapsed;
    }

    internal void RefreshScanningPresentation(bool motionEnabled, bool highContrast)
    {
        scanningMotionEnabled = motionEnabled;
        scanningHighContrast = highContrast;
        var play = attached && IsVisible && viewModel.IsScanningMapsets && motionEnabled && !highContrast && !scanningAnimationFailed;
        if (!play)
        {
            scanningAnimation?.StopAnimation();
            MapsetScanningAnimationHost.Visibility = Visibility.Collapsed;
            return;
        }
        if (scanningAnimation is null) LoadScanningAnimation(ScanningResourcePath);
        if (scanningAnimation is null) return;
        MapsetScanningAnimationHost.Visibility = Visibility.Visible;
        if (!scanningAnimation.IsPlaying) scanningAnimation.PlayAnimation();
    }

    private void OnScanningSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))) return;
        if (Dispatcher.CheckAccess()) RefreshScanningFromSystem();
        else Dispatcher.BeginInvoke(new Action(RefreshScanningFromSystem));
    }

    private void RefreshScanningFromSystem()
    {
        try
        {
            RefreshSongsRevealPolicy(SystemParameters.ClientAreaAnimation, SystemParameters.HighContrast);
            RefreshScanningPresentation(SystemParameters.ClientAreaAnimation, SystemParameters.HighContrast);
        }
        catch (Exception error)
        {
            Trace.TraceError("Mapset scan playback: {0}", error);
            scanningAnimationFailed = true;
            ReleaseScanningAnimation();
            MapsetScanningAnimationHost.Visibility = Visibility.Collapsed;
        }
    }

    private void ReleaseScanningAnimation()
    {
        var animation = scanningAnimation;
        scanningAnimation = null;
        MapsetScanningAnimationContent.Children.Clear();
        if (animation is null) return;
        animation.PlaybackFailed -= OnScanningPlaybackFailed;
        animation.StopAnimation();
        animation.Dispose();
    }
}
