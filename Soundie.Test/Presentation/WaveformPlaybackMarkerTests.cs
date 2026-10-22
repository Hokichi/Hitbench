using System.Windows;
using System.Windows.Media;
using Soundie.Core.Entities;
using Soundie.Resources.CustomControls;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WaveformPlaybackMarkerTests
{
    [TestCase(80d)]
    [TestCase(160d)]
    [TestCase(240d)]
    public void MarkerTracksTimeAndViewportAcrossResizeZoomAndScroll(double height)
    {
        var view = CreateView();
        foreach (var currentHeight in new[] { height, height + 40 })
        foreach (var zoom in new[] { 8d, 16d })
        foreach (var offset in new[] { 0d, 400d })
        {
            view.ZoomFactor = zoom;
            view.HorizontalOffset = offset;
            UiTestPump.Layout(view, 400 * zoom, currentHeight);
            view.PrepareDrawing();
            var marker = (DrawingVisual)VisualTreeHelper.GetChild(view, 1);
            Assert.That(marker.Drawing.Children, Is.Not.Empty);
            view.Position = TimeSpan.FromSeconds(1);
            view.PrepareDrawing();
            Assert.That(((TranslateTransform)marker.Transform).X,
                Is.EqualTo(400 * zoom / 10).Within(.001));
            var clip = ((RectangleGeometry)marker.Clip).Rect;
            Assert.That(clip.Width, Is.EqualTo(400));
            Assert.That(clip.X + ((TranslateTransform)marker.Transform).X, Is.EqualTo(offset).Within(0.001));
        }
    }

    [Test]
    public void PositionUpdatesMoveMarkerAndReuseContent()
    {
        var view = CreateView();
        UiTestPump.Layout(view, 3200, 160);
        view.PrepareDrawing();
        var content = view.ContentDrawing;
        var marker = (DrawingVisual)VisualTreeHelper.GetChild(view, 1);
        var originalX = ((TranslateTransform)marker.Transform).X;
        view.Position = TimeSpan.FromSeconds(1);
        view.PrepareDrawing();
        Assert.That(view.ContentDrawing, Is.SameAs(content));
        Assert.That(((TranslateTransform)marker.Transform).X, Is.GreaterThan(originalX));
        Assert.That(((TranslateTransform)marker.Transform).X, Is.EqualTo(320).Within(.001));
    }

    [TestCase("null")]
    [TestCase("duration")]
    [TestCase("peaks")]
    public void EmptyDataDrawsNoMarker(string kind)
    {
        var view = CreateView();
        view.Data = kind switch
        {
            "duration" => new WaveformData(TimeSpan.Zero, [0]),
            "peaks" => new WaveformData(TimeSpan.FromSeconds(10), []),
            _ => null!
        };
        UiTestPump.Layout(view, 3200, 160);
        view.PrepareDrawing();
        Assert.That(((DrawingVisual)VisualTreeHelper.GetChild(view, 1)).Drawing, Is.Null);
    }

    [Test]
    public void PlayedWaveformPositionChangesReusePeakContent()
    {
        var view = CreateView();
        UiTestPump.Layout(view, 3200, 160);
        view.PrepareDrawing();
        var content = view.ContentDrawing;
        foreach (var seconds in new[] { 1d, 8d, 0.5d, 0d, 10d })
        {
            view.Position = TimeSpan.FromSeconds(seconds);
            view.PrepareDrawing();
            Assert.That(view.ContentDrawing, Is.SameAs(content));
            Assert.That(view.GetPositionAt(1600), Is.EqualTo(TimeSpan.FromSeconds(5)));
        }
    }

    private static WaveformView CreateView() => new()
    {
        Data = new WaveformData(TimeSpan.FromSeconds(10), [0, 0.5f, 1]),
        WaveformBrush = Brushes.Blue,
        MarkerBrush = Brushes.Red,
        ViewportWidth = 400,
        ZoomFactor = 8
    };
}
