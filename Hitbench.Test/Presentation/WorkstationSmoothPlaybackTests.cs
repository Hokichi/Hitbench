using System.Windows;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationSmoothPlaybackTests
{
    [SetUp]
    public void Setup() => new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();

    [Test]
    public void WaveformRetainsContentAcrossFractionalOffsets()
    {
        var waveform = new WaveformView { Data = new WaveformData(TimeSpan.FromSeconds(10), Enumerable.Repeat(.5f, 1000).ToArray()),
            ViewportWidth = 200, ZoomFactor = 8, IsPlaybackMarkerStatic = true, HorizontalOffset = 400 };
        UiTestPump.Layout(waveform, 1600, 100); waveform.PrepareDrawing();
        var drawing = waveform.ContentDrawing;
        var builds = waveform.ContentBuildCount;
        for (var i = 1; i <= 400; i++) { waveform.HorizontalOffset = 400 + i / 4d; waveform.PrepareDrawing(); }
        Assert.That(waveform.ContentDrawing, Is.SameAs(drawing)); Assert.That(waveform.ContentBuildCount, Is.EqualTo(builds));
        waveform.HorizontalOffset = 500.25; waveform.PrepareDrawing();
        Assert.That(waveform.ContentBuildCount, Is.EqualTo(builds + 1));
    }
}
