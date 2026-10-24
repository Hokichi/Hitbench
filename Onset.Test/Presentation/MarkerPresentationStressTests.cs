using System.Windows;
using System.Windows.Media;
using Onset.Core.Entities;
using Onset.Resources.CustomControls;
using System.Diagnostics;
using Onset.Test.Services;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class MarkerPresentationStressTests
{
    [TestCase(64), TestCase(128), Explicit]
    public void MeasureMarkerPresentation(int tracks)
    {
        var events = MarkerStressFixture.Create(tracks);
        var views = Enumerable.Range(0, tracks).Select(track => new BeatGridView
        {
            Duration = TimeSpan.FromMinutes(5), ContentWidth = 10240, ViewportWidth = 640,
            Offsets = [0, 100000, 200000], Bpms = [60, 120, 240], PlaybackMarkerBrush = Brushes.White,
            Samples = events.Skip(track * 1000).Take(1000).Select(e => new Sample { Timestamp = e.Timestamp }).ToArray()
        }).ToArray();
        foreach (var view in views) { UiTestPump.Layout(view, 640, 72); view.PrepareDrawing(); }
        var retained = views.Select(v => v.ContentDrawing).ToArray();
        var durations = new List<double>();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 18000; frame++)
        {
            var time = TimeSpan.FromMilliseconds(frame * 1000d / 60);
            var start = Stopwatch.GetTimestamp();
            foreach (var view in views) { view.Position = time; view.PrepareDrawing(); }
            if (frame >= 60) durations.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        TestContext.Out.WriteLine($"all-realized-tracks={tracks}; bytes={GC.GetAllocatedBytesForCurrentThread() - allocated}; no compositor/FPS measurement");
        MarkerPlaybackStressTests.Report("marker-application", durations);
        Assert.That(views.Select(v => v.ContentDrawing), Is.EqualTo(retained));
    }

    [Test]
    public void RecycledTrackAndThemeUseCurrentGeometryAndMarker()
    {
        var grid = new BeatGridView { Duration = TimeSpan.FromSeconds(10), ContentWidth = 640, ViewportWidth = 640,
            Offsets = [0], Bpms = [120], Position = TimeSpan.FromSeconds(1), PlaybackMarkerBrush = Brushes.White };
        UiTestPump.Layout(grid, 640, 72);
        grid.PrepareDrawing();
        var old = grid.ContentDrawing;
        grid.DataContext = new Track();
        grid.Samples = [new Sample { Timestamp = 300 }];
        grid.PlaybackMarkerBrush = Brushes.Black;
        grid.PrepareDrawing();
        Assert.That(grid.ContentDrawing, Is.Not.SameAs(old));
        Assert.That(grid.GetPlaybackMarkerX(), Is.EqualTo(64));
    }
    [Test]
    public void WaveformPositionRetainsContentAndViewportInvalidatesIt()
    {
        var wave = new WaveformView { Data = new WaveformData(TimeSpan.FromSeconds(10), [.1f, .5f]),
            ViewportWidth = 640, FontFamily = SystemFonts.MessageFontFamily, Offsets = [0], Bpms = [120] };
        UiTestPump.Layout(wave, 640, 120);
        wave.PrepareDrawing();
        var content = wave.ContentDrawing;
        wave.Position = TimeSpan.FromSeconds(1);
        wave.PrepareDrawing();
        Assert.That(wave.ContentDrawing, Is.SameAs(content));
        wave.HorizontalOffset = 100;
        wave.PrepareDrawing();
        Assert.That(wave.ContentDrawing, Is.Not.SameAs(content));
    }

    [Test]
    public void TrackPositionRetainsContentAndSampleRevisionInvalidatesIt()
    {
        var grid = new BeatGridView { Duration = TimeSpan.FromSeconds(10), ContentWidth = 640, ViewportWidth = 640,
            Offsets = [0], Bpms = [120], PlaybackMarkerBrush = Brushes.White, Samples = [new Sample { Timestamp = 100 }] };
        UiTestPump.Layout(grid, 640, 72);
        grid.PrepareDrawing();
        var content = grid.ContentDrawing;
        grid.Position = TimeSpan.FromSeconds(1);
        grid.PrepareDrawing();
        Assert.That(grid.ContentDrawing, Is.SameAs(content));
        Assert.That(grid.GetPlaybackMarkerX(), Is.EqualTo(64));
        grid.SamplesRevision++;
        grid.PrepareDrawing();
        Assert.That(grid.ContentDrawing, Is.Not.SameAs(content));
    }
}
