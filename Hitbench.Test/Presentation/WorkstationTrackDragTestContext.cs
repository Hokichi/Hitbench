using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationTrackDragTestContext : IDisposable
{
    public WorkstationPageVM Vm { get; }
    public OHsWorkstationTrackList Owner { get; }
    public ListBox List { get; }
    public InMemoryView View { get; }
    public DelayedProjectFileService Saves { get; } = new();
    public ImageSource Preview { get; } = new DrawingImage();
    public int Captures { get; private set; }
    public int Releases { get; private set; }
    public bool CanCapture { get; set; } = true;

    public WorkstationTrackDragTestContext(int count = 4, string theme = "Dark", FlowDirection direction = FlowDirection.LeftToRight)
    {
        new VolumeWheelTests().SetUp();
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Hitbench;component/Resources/Styles/Image.xaml", UriKind.Relative) });
        if (theme != "Dark") Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var tracks = Enumerable.Range(0, count).Select(i => i % 2 == 0
            ? new Track { Name = $"Track {i}", IsCollapsed = true, SelectedSampleFileName = "hit.wav" }
            : new InstrumentalTrack { Name = $"Track {i}", IsCollapsed = true }).ToList();
        Saves.Release();
        Vm = new WorkstationPageVM(new StubWorkstationSourceService
        { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: Saves);
        UiTestPump.Run(Vm.LoadAsync("project.json", default));
        Vm.AcknowledgePresentationReady(Vm.LoadGeneration);
        Vm.IsAdditionMode = false;
        Owner = new OHsWorkstationTrackList { DataContext = Vm, FlowDirection = direction };
        Owner.CaptureTrackPointer = () => { Captures++; return CanCapture; };
        Owner.ReleaseTrackPointer = () => Releases++;
        Owner.CaptureTrackPreview = _ => Preview;
        List = (ListBox)Owner.FindName("TrackList");
        View = new InMemoryView(new Window { Content = Owner, Width = 1100, Height = 600 }, scheduleLayout: false);
        View.UpdateLayout();
        UiTestPump.Run(Vm.FlushSavesAsync());
    }

    public OHsWorkstationTrackRow Row(int index) => Descendants<OHsWorkstationTrackRow>((DependencyObject)List.ItemContainerGenerator.ContainerFromIndex(index)).Single();
    public Point PointOn(OHsWorkstationTrackRow row) => row.TranslatePoint(new Point(2, 2), Owner);
    public Point Start(int index = 0)
    {
        var row = Row(index); var point = PointOn(row);
        Assert.That(Owner.BeginTrackPointer(row, point), Is.True);
        Owner.UpdateTrackPointer(new Point(point.X + SystemParameters.MinimumHorizontalDragDistance + 1, point.Y), true);
        View.UpdateLayout();
        return point;
    }
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    public void Dispose()
    {
        Owner.Deactivate();
        View.Dispose();
        UiTestPump.Run(Vm.DeactivateAsync());
    }
}
