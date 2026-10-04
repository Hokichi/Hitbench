using System.Windows;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WaveformPlaybackMarkerTests
{
    [TestCase(80d)]
    [TestCase(160d)]
    [TestCase(240d)]
    public void MarkerReachesBottomAcrossResizeZoomAndScroll(double height)
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
            var lines = marker.Drawing.Children.OfType<GeometryDrawing>().Select(d => (LineGeometry)d.Geometry).ToArray();
            Assert.That(lines, Is.Not.Empty);
            Assert.That(lines.All(line => line.StartPoint == new Point(0, 0) &&
                line.EndPoint == new Point(0, currentHeight)), Is.True);
            var clip = ((RectangleGeometry)marker.Clip).Rect;
            Assert.That(clip.Height, Is.EqualTo(currentHeight));
            Assert.That(clip.Width, Is.EqualTo(400));
            Assert.That(clip.X + ((TranslateTransform)marker.Transform).X, Is.EqualTo(offset).Within(0.001));
        }
    }

    [Test]
    public void PositionUpdatesRetainFullHeightAndReuseContent()
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
        Assert.That(marker.Drawing.Children.OfType<GeometryDrawing>()
            .All(d => ((LineGeometry)d.Geometry).EndPoint.Y == 160), Is.True);
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

    private static WaveformView CreateView() => new()
    {
        Data = new WaveformData(TimeSpan.FromSeconds(10), [0, 0.5f, 1]),
        WaveformBrush = Brushes.Blue,
        MarkerBrush = Brushes.Red,
        ViewportWidth = 400,
        ZoomFactor = 8
    };
}
