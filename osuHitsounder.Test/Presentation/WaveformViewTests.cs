using System.Windows;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WaveformViewTests
{
    [Test]
    public void DesiredWidthTracksViewportAndZoom()
    {
        var view = CreateView();
        view.ViewportWidth = 400;
        view.ZoomFactor = 4;
        view.Measure(new Size(double.PositiveInfinity, 160));
        Assert.That(view.DesiredSize.Width, Is.EqualTo(1600));
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

    private static WaveformView CreateView() => new()
    {
        Data = new WaveformData(TimeSpan.FromSeconds(10), [0, 0.5f, 1, 0.25f]),
        WaveformBrush = Brushes.DodgerBlue,
        MarkerBrush = Brushes.Red,
        TextBrush = Brushes.White,
        FontFamily = new FontFamily("Arial")
    };
}
