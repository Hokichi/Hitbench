using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WaveformControlPointMarkerTests
{
    private static WaveformView Create() => new()
    {
        Data = new WaveformData(TimeSpan.FromSeconds(10), [0, 0]),
        ViewportWidth = 400, ZoomFactor = 8,
        ControlPointTimestamps = [-1, 0, 1000, 1250, 1500, 2500, 2500, 10000, 10001]
    };
    private static IEnumerable<LineGeometry> Lines(Drawing drawing)
    {
        if (drawing is GeometryDrawing { Geometry: LineGeometry line } && line.StartPoint.X == line.EndPoint.X && line.EndPoint.Y > line.StartPoint.Y)
            yield return line;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var descendant in Lines(child)) yield return descendant;
    }

    [TestCase(8, 0)][TestCase(8, 400)][TestCase(16, 400)][TestCase(16, 6000)]
    public void DistinctMarkersFollowTimestampMappingAndVisibleViewport(double zoom, double offset)
    {
        var view = Create(); view.ZoomFactor = zoom; view.HorizontalOffset = offset;
        var width = 400 * zoom;
        UiTestPump.Layout(view, width, 160); view.PrepareDrawing();
        var expected = new[] { 0, 1000, 1250, 1500, 2500, 10000 }
            .Select(timestamp => timestamp / 10000d * width).Where(x => x >= offset && x <= offset + 400).ToArray();
        var lines = Lines(view.ContentDrawing!).ToArray();
        Assert.That(lines.Select(line => line.StartPoint.X), Is.EqualTo(expected).Within(.001));
        foreach (var line in lines)
            Assert.That(view.GetPositionAt(line.StartPoint.X)!.Value.TotalMilliseconds,
                Is.EqualTo(line.StartPoint.X / width * 10000).Within(.001));
    }

    [Test]
    public void TimestampReplacementDeletionAndResizeInvalidateContentWhilePlaybackReusesIt()
    {
        var view = Create(); view.ControlPointTimestamps = [1000]; UiTestPump.Layout(view, 3200, 160);
        view.PrepareDrawing(); var original = view.ContentDrawing;
        Assert.That(Lines(original!).Single().StartPoint.X, Is.EqualTo(320));
        view.HorizontalOffset = 400; view.ControlPointTimestamps = [2000]; view.PrepareDrawing();
        Assert.That(view.ContentDrawing, Is.Not.SameAs(original));
        Assert.That(Lines(view.ContentDrawing!).Single().StartPoint.X, Is.EqualTo(640));
        var updated = view.ContentDrawing;
        view.Position = TimeSpan.FromSeconds(3); view.PrepareDrawing();
        Assert.That(view.ContentDrawing, Is.SameAs(updated));
        view.ZoomFactor = 16; UiTestPump.Layout(view, 6400, 240); view.HorizontalOffset = 1000; view.PrepareDrawing();
        Assert.That(Lines(view.ContentDrawing!).Single().StartPoint.X, Is.EqualTo(1280));
        view.ControlPointTimestamps = []; view.PrepareDrawing(); Assert.That(Lines(view.ContentDrawing!), Is.Empty);
    }

    [TestCase("null")][TestCase("duration")][TestCase("peaks")]
    public void MissingWaveformProducesNoTimestampMarkers(string kind)
    {
        var view = Create(); view.Data = kind switch
        { "duration" => new(TimeSpan.Zero, [0]), "peaks" => new(TimeSpan.FromSeconds(10), []), _ => null };
        UiTestPump.Layout(view, 3200, 160); Assert.DoesNotThrow(view.PrepareDrawing);
        Assert.That(Lines(view.ContentDrawing!), Is.Empty);
    }

    [Test]
    public void RealPageBindsLoadedAndMutatedOwnerTimestampsOneWay()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 1000, Volume = 50 }] };
        var messages = new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger();
        var dialogs = new StubControlPointsDialogService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messages, controlPointsDialog: dialogs);
        try
        {
            UiTestPump.Run(owner.LoadAsync("project.json", default)); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var page = new WorkstationPage(owner); UiTestPump.Layout(page, 900, 600);
            var view = (WaveformView)page.FindName("WaveformDisplay");
            Assert.That(BindingOperations.GetBinding(view, WaveformView.ControlPointTimestampsProperty)!.Mode, Is.EqualTo(BindingMode.OneWay));
            Assert.That(view.ControlPointTimestamps, Is.EqualTo(new[] { 1000 }));
            dialogs.Show = (initial, _) =>
            {
                messages.Send(new osuHitsounder.Resources.Messages.DeleteControlPointRequestedMessage(initial.Context, initial.Points[0].Id));
                Assert.That(view.ControlPointTimestamps, Is.Empty);
                return Task.CompletedTask;
            };
            UiTestPump.Run(owner.AddControlPointCommand.ExecuteAsync(null)); Assert.That(owner.ErrorMessage, Is.Null);
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }
}
