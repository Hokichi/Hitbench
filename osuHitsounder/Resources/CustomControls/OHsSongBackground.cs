using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Resources.CustomControls;

public sealed class OHsSongBackground : Border
{
    private const double BlurredRadius = 10;
    private static readonly Duration BlurDuration = new(TimeSpan.FromMilliseconds(100));
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path), typeof(string), typeof(OHsSongBackground), new PropertyMetadata("", OnSourceChanged));
    public static readonly DependencyProperty LoaderProperty = DependencyProperty.Register(
        nameof(Loader), typeof(SongImageLoader), typeof(OHsSongBackground), new PropertyMetadata(null, OnSourceChanged));
    private static readonly DependencyPropertyKey ImageBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ImageBrush), typeof(Brush), typeof(OHsSongBackground), new PropertyMetadata(null));
    public static readonly DependencyProperty ImageBrushProperty = ImageBrushPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsBlurredProperty = DependencyProperty.Register(
        nameof(IsBlurred), typeof(bool), typeof(OHsSongBackground),
        new PropertyMetadata(true, OnIsBlurredChanged));
    private readonly BlurEffect blur = new() { Radius = BlurredRadius };
    private CancellationTokenSource? loading;

    public OHsSongBackground()
    {
        Effect = blur;
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => Cancel();
    }

    public string Path { get => (string)GetValue(PathProperty); set => SetValue(PathProperty, value); }
    public SongImageLoader? Loader { get => (SongImageLoader?)GetValue(LoaderProperty); set => SetValue(LoaderProperty, value); }
    public Brush? ImageBrush => (Brush?)GetValue(ImageBrushProperty);
    public bool IsBlurred { get => (bool)GetValue(IsBlurredProperty); set => SetValue(IsBlurredProperty, value); }
    public Task LoadingTask { get; private set; } = Task.CompletedTask;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Restart();
    }

    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((OHsSongBackground)sender).Restart();

    private static void OnIsBlurredChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((OHsSongBackground)sender).UpdateBlur((bool)args.NewValue);

    private void UpdateBlur(bool isBlurred)
    {
        var target = isBlurred ? BlurredRadius : 0;
        var current = blur.Radius;
        blur.BeginAnimation(BlurEffect.RadiusProperty, null);
        blur.Radius = target;
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(current, target, BlurDuration)
        {
            EasingFunction = new QuadraticEase
            {
                EasingMode = isBlurred ? EasingMode.EaseIn : EasingMode.EaseOut
            },
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void Cancel()
    {
        loading?.Cancel();
        loading = null;
        SetValue(ImageBrushPropertyKey, null);
    }

    private void Restart()
    {
        Cancel();
        if (!IsLoaded || Loader is null) return;
        var source = new CancellationTokenSource();
        loading = source;
        LoadingTask = LoadAsync(Loader, Path, source);
    }

    private async Task LoadAsync(SongImageLoader loader, string path, CancellationTokenSource source)
    {
        try
        {
            var width = (int)Math.Ceiling(336 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            var image = await loader.LoadAsync(path, width, source.Token);
            if (!ReferenceEquals(loading, source) || source.IsCancellationRequested || !IsLoaded) return;
            if (image is not null)
            {
                var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                SetValue(ImageBrushPropertyKey, brush);
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception error)
        {
            Trace.TraceError("Song background loading failed: {0}", error);
        }
        finally
        {
            if (ReferenceEquals(loading, source)) loading = null;
            source.Dispose();
        }
    }
}
