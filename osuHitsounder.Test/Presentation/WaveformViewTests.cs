using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WaveformViewTests
{
    [Test]
    public void ContentHasNoSecondRulerOrTimestampLabels()
    {
        var view = CreateView();
        view.ViewportWidth = 400;
        view.Measure(new Size(double.PositiveInfinity, 160));
        view.Arrange(new Rect(0, 0, 3200, 160));
        view.PrepareDrawing();
        var drawings = Flatten(view.ContentDrawing!).ToArray();
        Assert.That(drawings.OfType<GlyphRunDrawing>(), Is.Empty);
        Assert.That(drawings.OfType<GeometryDrawing>()
            .Where(d => d.Geometry is LineGeometry line && line.StartPoint.Y >= 140), Is.Empty);

        static IEnumerable<Drawing> Flatten(Drawing drawing)
        {
            yield return drawing;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var descendant in Flatten(child)) yield return descendant;
        }
    }
    [TestCase(8d, 3200d)]
    [TestCase(32d, 12800d)]
    [TestCase(1d, 3200d)]
    [TestCase(64d, 25600d)]
    [TestCase(128d, 51200d)]
    [TestCase(256d, 51200d)]
    [TestCase(-2d, 3200d)]
    public void MeasurementSupportsWorkstationZoomUpTo128(double zoom, double width)
    {
        var view = CreateView();
        view.ViewportWidth = 400;
        view.ZoomFactor = zoom;
        view.Measure(new Size(double.PositiveInfinity, 160));
        Assert.That(view.DesiredSize.Width, Is.EqualTo(width));
    }

    [Test]
    public void DesiredWidthTracksViewportAndZoom()
    {
        var view = CreateView();
        view.ViewportWidth = 400;
        view.ZoomFactor = 8;
        view.Measure(new Size(double.PositiveInfinity, 160));
        Assert.That(view.DesiredSize.Width, Is.EqualTo(3200));
        Assert.That(view.DesiredSize.Height, Is.EqualTo(160));
    }

    [Test]
    public void ResizePreservesZoomFactorAndUpdatesWidth()
    {
        var view = CreateView();
        view.ViewportWidth = 250;
        view.ZoomFactor = 16;
        view.Measure(new Size(double.PositiveInfinity, 120));
        Assert.That(view.DesiredSize.Width, Is.EqualTo(4000));
        view.ViewportWidth = 500;
        view.Measure(new Size(double.PositiveInfinity, 120));
        Assert.That(view.DesiredSize.Width, Is.EqualTo(8000));
        Assert.That(view.ZoomFactor, Is.EqualTo(16));
    }

    [Test]
    public void EmptyOrInvalidInputsStayFinite()
    {
        var view = CreateView();
        view.ViewportWidth = double.NaN;
        view.ZoomFactor = -2;
        view.HorizontalOffset = double.PositiveInfinity;
        view.Measure(new Size(double.PositiveInfinity, 100));
        Assert.That(double.IsFinite(view.DesiredSize.Width), Is.True);
        Assert.DoesNotThrow(() => view.Arrange(new Rect(view.DesiredSize)));
    }

    [Test]
    public void MapsContentCoordinatesAndExecutesSeekCommand()
    {
        var sought = TimeSpan.MinValue;
        var view = CreateView();
        view.ViewportWidth = 400;
        view.ZoomFactor = 8;
        view.SeekCommand = new RelayCommand<object?>(value => sought = (TimeSpan)value!);
        view.Measure(new Size(double.PositiveInfinity, 160));
        view.Arrange(new Rect(0, 0, 3200, 160));
        using var source = new InMemoryPresentationSource { RootVisual = view };

        Assert.Multiple(() =>
        {
            Assert.That(view.GetPositionAt(1600), Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(view.GetPositionAt(-10), Is.EqualTo(TimeSpan.Zero));
            Assert.That(view.GetPositionAt(4000), Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(view.SeekAt(800), Is.True);
            Assert.That(sought, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
            Assert.That(view.InputHitTest(new Point(300, 80)), Is.Not.Null);
        });
    }

    [Test]
    public void InvalidLayoutOrDisabledCommandDoesNotSeek()
    {
        var executions = 0;
        var view = CreateView();
        view.SeekCommand = new RelayCommand<object?>(_ => executions++, _ => false);
        Assert.That(view.GetPositionAt(1), Is.Null);
        view.Measure(new Size(400, 160));
        view.Arrange(new Rect(0, 0, 400, 160));
        Assert.That(view.SeekAt(200), Is.False);
        Assert.That(executions, Is.Zero);
        view.Data = null;
        Assert.That(view.GetPositionAt(200), Is.Null);
    }

    private static WaveformView CreateView() => new()
    {
        Data = new WaveformData(TimeSpan.FromSeconds(10), [0, 0.5f, 1, 0.25f]),
        WaveformBrush = Brushes.DodgerBlue,
        MarkerBrush = Brushes.Red,
        TextBrush = Brushes.White,
        FontFamily = new FontFamily("Arial")
    };
}
