using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Views.Pages;

public partial class HomePage
{
    // Thumbnail decoding stays independent of layout; WPF owns displayed image dimensions.
    private const int SongImageDecodeWidth = 336;
    private readonly Dictionary<Image, SongImageRequest> imageRequests = [];
    private readonly Dictionary<Image, Button> imageOwners = [];
    private readonly HashSet<Image> projectImages = [];

    private void OnProjectImageLoaded(object sender, RoutedEventArgs e)
    {
        var image = (Image)sender;
        if (lifetime.IsCancellationRequested || !projectImages.Add(image)) return;
        StartProjectImage(image);
    }

    private void OnProjectImageUnloaded(object sender, RoutedEventArgs e)
    {
        var image = (Image)sender;
        projectImages.Remove(image);
        CancelSongImage(image);
    }

    private void OnProjectImageDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var image = (Image)sender;
        CancelSongImage(image);
        if (projectImages.Contains(image)) StartProjectImage(image);
    }

    private void StartProjectImage(Image image)
    {
        var request = new SongImageRequest();
        imageRequests.Add(image, request);
        request.LoadingTask = LoadSongImageAsync(image, (image.DataContext as FolderEntry)?.BackgroundPath ?? "", request, 64);
    }

    private void ReleaseProjectImages()
    {
        foreach (var image in projectImages.ToArray()) OnProjectImageUnloaded(image, new RoutedEventArgs());
    }

    private void OnSongImageLoaded(object sender, RoutedEventArgs e)
    {
        var image = (Image)sender;
        if (lifetime.IsCancellationRequested || imageOwners.ContainsKey(image)) return;
        DependencyObject? parent = image;
        while (parent is not null && parent is not Button) parent = VisualTreeHelper.GetParent(parent);
        if (parent is not Button button) return;
        imageOwners.Add(image, button);
        image.Effect = new BlurEffect { Radius = button.IsMouseOver || button.IsKeyboardFocusWithin ? 0 : 10 };
        button.MouseEnter += OnSongMouseEnter;
        button.MouseLeave += OnSongMouseLeave;
        button.IsKeyboardFocusWithinChanged += OnSongFocusChanged;
        StartSongImage(image);
    }

    private void OnSongImageUnloaded(object sender, RoutedEventArgs e) => ReleaseSongImage((Image)sender);

    private void OnSongImageDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var image = (Image)sender;
        CancelSongImage(image);
        if (imageOwners.ContainsKey(image)) StartSongImage(image);
    }

    private void OnSongCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var card = (Grid)sender;
        card.Clip = new RectangleGeometry(new Rect(card.RenderSize), 7, 7);
    }

    private void StartSongImage(Image image)
    {
        if (lifetime.IsCancellationRequested || !imageOwners.ContainsKey(image)) return;
        if (imageRequests.ContainsKey(image)) return;
        CancelSongImage(image);
        var request = new SongImageRequest();
        imageRequests.Add(image, request);
        request.LoadingTask = LoadSongImageAsync(image, (image.DataContext as FolderEntry)?.BackgroundPath ?? "", request);
    }

    private async Task LoadSongImageAsync(Image image, string path, SongImageRequest request, int decodeWidth = SongImageDecodeWidth)
    {
        var cancellation = request.Cancellation!;
        try
        {
            var source = await ImageLoader.LoadAsync(path, decodeWidth, cancellation.Token);
            if (!cancellation.IsCancellationRequested && imageRequests.TryGetValue(image, out var current) &&
                ReferenceEquals(current, request)) image.Source = source;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { Trace.TraceError("Song background loading failed: {0}", error); }
        finally
        {
            request.Cancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelSongImage(Image image)
    {
        if (imageRequests.Remove(image, out var request)) request.Cancellation?.Cancel();
        image.Source = null;
    }

    private void ReleaseSongImage(Image image)
    {
        CancelSongImage(image);
        if (!imageOwners.Remove(image, out var button)) return;
        button.MouseEnter -= OnSongMouseEnter;
        button.MouseLeave -= OnSongMouseLeave;
        button.IsKeyboardFocusWithinChanged -= OnSongFocusChanged;
        if (image.Effect is BlurEffect blur) blur.BeginAnimation(BlurEffect.RadiusProperty, null);
    }

    private void ReleaseSongImages()
    {
        foreach (var image in imageOwners.Keys.ToArray()) ReleaseSongImage(image);
    }

    private void OnSongMouseEnter(object sender, MouseEventArgs e) => SetSongBlur((Button)sender, false);
    private void OnSongMouseLeave(object sender, MouseEventArgs e) => SetSongBlur((Button)sender, !((Button)sender).IsKeyboardFocusWithin);
    private void OnSongFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var button = (Button)sender;
        SetSongBlur(button, !button.IsMouseOver && !button.IsKeyboardFocusWithin);
    }

    private void SetSongBlur(Button button, bool blurred)
    {
        foreach (var image in imageOwners.Where(pair => ReferenceEquals(pair.Value, button)).Select(pair => pair.Key))
        {
            if (image.Effect is not BlurEffect blur) continue;
            var target = blurred ? 10d : 0d;
            var current = blur.Radius;
            blur.BeginAnimation(BlurEffect.RadiusProperty, null);
            blur.Radius = target;
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) continue;
            blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = blurred ? EasingMode.EaseIn : EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }
    }
}
