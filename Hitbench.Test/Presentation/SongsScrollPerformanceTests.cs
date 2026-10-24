using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class SongsScrollPerformanceTests
{
    [Test]
    public void MeasureScrollWorkloads()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var path = Path.GetTempFileName();
        try
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(336, 108, 96, 96,
                PixelFormats.Bgra32, null, new byte[336 * 108 * 4], 336 * 4)));
            using (var stream = File.Create(path)) png.Save(stream);
            foreach (var count in new[] { 2000, 20000 })
            {
                var messenger = new WeakReferenceMessenger();
                var settings = new StubAppSettingsService();
                var vm = new HomePageVM(new StubHomeFolderService(), settings,
                    new StubInstallationDialogService(), new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
                    new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
                for (var i = 0; i < count; i++)
                    vm.Songs.Add(new FolderEntry($@"C:\Songs\{i}", i.ToString(), DateTimeOffset.MinValue) { BackgroundPath = path });
                var page = new HomePage(vm);
                UiTestPump.Run(page.ImageLoader.LoadAsync(path, 336, CancellationToken.None));
                var window = TestShell.Create(page, messenger, WorkstationTestPage.Create, vm, settings);
                window.Width = 1922;
                window.Height = 1082;
                using var view = new InMemoryView(window, scheduleLayout: false);
                page.UpdateSongsLayout(new Size(1920, 1040));
                view.UpdateLayout();
                Drain();
                var panel = FindPanel(page);
                foreach (var workload in new[] { "pixel", "crossing", "traversal", "burst", "panel-pixel" })
                {
                    var times = new List<double>();
                    long allocation = 0, measures = 0, realizations = 0, prepared = 0, recycled = 0, pageSizing = 0, shellSizing = 0;
                    int peak = 0;
                    double Offset(int step) => workload switch
                    {
                        "pixel" or "panel-pixel" => 3 * panel.RowStride + (step % 2 == 0 ? .25 : .75),
                        "crossing" => (step % 40) * panel.RowStride + .25,
                        "traversal" => (step % 128 < 64 ? step % 64 : 63 - step % 64) / 63d * Math.Max(0, panel.ExtentHeight - panel.ViewportHeight),
                        _ => (step % 40) * 3 * panel.RowStride
                    };
                    void Step(int step)
                    {
                        panel.SetVerticalOffset(Offset(step));
                        if (workload == "panel-pixel") panel.UpdateLayout();
                        else { UiTestPump.Layout(view.Root, window.Width, window.Height); Drain(); }
                    }
                    for (var sample = 0; sample < 5; sample++)
                    {
                        for (var step = 0; step < 64; step++) Step(step);
                        view.UpdateLayout();
                        Drain();
                        var m = panel.MeasurePassCount;
                        var r = panel.RealizationPassCount;
                        var p = panel.PreparedContainerCount;
                        var c = panel.RecycledContainerCount;
                        var h = page.SongsLayoutCalculationCount;
                        var s = window.SongsLayoutRefreshCount;
                        var allocated = GC.GetAllocatedBytesForCurrentThread();
                        for (var step = 0; step < 512; step++)
                        {
                            var started = Stopwatch.GetTimestamp();
                            Step(step);
                            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                            peak = Math.Max(peak, panel.Children.Count);
                        }
                        allocation += GC.GetAllocatedBytesForCurrentThread() - allocated;
                        measures += panel.MeasurePassCount - m;
                        realizations += panel.RealizationPassCount - r;
                        prepared += panel.PreparedContainerCount - p;
                        recycled += panel.RecycledContainerCount - c;
                        pageSizing += page.SongsLayoutCalculationCount - h;
                        shellSizing += window.SongsLayoutRefreshCount - s;
                    }
                    times.Sort();
                    TestContext.Out.WriteLine($"PERF {count} {workload}: median={times[times.Count / 2]:F4}ms p95={times[(int)(times.Count * .95)]:F4}ms bytes/step={allocation / times.Count} measure={measures} realize={realizations} prepare={prepared} recycle={recycled} page={pageSizing} shell={shellSizing} peak={peak}");
                    Assert.That(peak, Is.LessThanOrEqualTo((panel.VisibleRowCount + 3) * panel.Columns));
                }
                window.Close();
            }
        }
        finally { File.Delete(path); }
    }

    internal static SpacedUniformGrid FindPanel(DependencyObject parent)
    {
        if (parent is SpacedUniformGrid panel) return panel;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            try { return FindPanel(VisualTreeHelper.GetChild(parent, i)); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Songs panel missing.");
    }

    internal static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
}
