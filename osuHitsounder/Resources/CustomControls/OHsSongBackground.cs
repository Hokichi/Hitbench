using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Resources.CustomControls;

public sealed class OHsSongBackground : Border
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path), typeof(string), typeof(OHsSongBackground), new PropertyMetadata("", OnSourceChanged));
    public static readonly DependencyProperty LoaderProperty = DependencyProperty.Register(
        nameof(Loader), typeof(SongImageLoader), typeof(OHsSongBackground), new PropertyMetadata(null, OnSourceChanged));
    private static readonly DependencyPropertyKey ImageBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ImageBrush), typeof(Brush), typeof(OHsSongBackground), new PropertyMetadata(null));
    public static readonly DependencyProperty ImageBrushProperty = ImageBrushPropertyKey.DependencyProperty;
    private CancellationTokenSource? loading;

    public OHsSongBackground()
    {
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => Cancel();
    }

    public string Path { get => (string)GetValue(PathProperty); set => SetValue(PathProperty, value); }
    public SongImageLoader? Loader { get => (SongImageLoader?)GetValue(LoaderProperty); set => SetValue(LoaderProperty, value); }
    public Brush? ImageBrush => (Brush?)GetValue(ImageBrushProperty);
    public Task LoadingTask { get; private set; } = Task.CompletedTask;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Restart();
    }

    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((OHsSongBackground)sender).Restart();

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
