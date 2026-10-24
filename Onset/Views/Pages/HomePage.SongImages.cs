using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Onset.Resources.DataModels;
using Onset.Services.Ui;

namespace Onset.Views.Pages;

public partial class HomePage
{
    // Thumbnail decoding stays independent of layout; WPF owns displayed image dimensions.
    private const int SongImageDecodeWidth = 336;
    private readonly Dictionary<Image, SongImageRequest> imageRequests = [];
    private readonly HashSet<Image> loadedSongImages = [];
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
        if (lifetime.IsCancellationRequested || !loadedSongImages.Add(image)) return;
        StartSongImage(image);
    }

    private void OnSongImageUnloaded(object sender, RoutedEventArgs e) => ReleaseSongImage((Image)sender);

    private void OnSongImageDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var image = (Image)sender;
        CancelSongImage(image);
        if (loadedSongImages.Contains(image)) StartSongImage(image);
    }

    private void OnSongCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var card = (Grid)sender;
        card.Clip = new RectangleGeometry(new Rect(card.RenderSize), 7, 7);
    }

    private void StartSongImage(Image image)
    {
        if (lifetime.IsCancellationRequested || !loadedSongImages.Contains(image)) return;
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
        loadedSongImages.Remove(image);
    }

    private void ReleaseSongImages()
    {
        foreach (var image in loadedSongImages.ToArray()) ReleaseSongImage(image);
    }

}
