using System.Diagnostics;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;
using Hitbench.Resources.DataModels;
using Hitbench.Test.Presentation;
using Hitbench.ViewModels.Shell;
using Hitbench.ViewModels.Shell.HomePage;
using CommunityToolkit.Mvvm.Messaging;
using NAudio.Wave;
namespace Hitbench.Test.Services;
[Explicit("Local comparative measurements, no CI latency threshold")]
public sealed class UxPerformanceMeasurements
{
    [Test]
    public async Task MeasureRepeatedSampleReads()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1))) writer.Write(new byte[88200], 0, 88200);
            var cold = new List<double>(); var warm = new List<double>(); var allocations = new List<long>();
            for (var run = 0; run < 6; run++)
            {
                var opens = 0;
                using var browser = new SampleBrowserService(openDecoder: p => { opens++; return new WaveFileReader(p); });
                var timer = Stopwatch.StartNew();
                await browser.ReadMetadataAsync(path, default); await browser.ReadWaveformAsync(path, default);
                var elapsed = timer.Elapsed.TotalMilliseconds;
                var before = GC.GetTotalAllocatedBytes(true); timer.Restart();
                await browser.ReadMetadataAsync(path, default); await browser.ReadWaveformAsync(path, default);
                if (run > 0) { cold.Add(elapsed); warm.Add(timer.Elapsed.TotalMilliseconds); allocations.Add(GC.GetTotalAllocatedBytes(true) - before); }
                Assert.That(opens, Is.EqualTo(2));
            }
            cold.Sort(); warm.Sort(); allocations.Sort();
            TestContext.Out.WriteLine($"Sample metadata + waveform: cold median={cold[2]:F3}ms; cached median={warm[2]:F3}ms; cached process allocations median={allocations[2]} bytes; decoder opens=2 cold + 0 cached (five runs after warmup).");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task MeasureSnapshotBurst()
    {
        var eager = new List<double>(); var coalesced = new List<double>(); var allocations = new List<long>();
        for (var run = 0; run < 6; run++)
        {
            var project = new ProjectData { Tracks = [new Track { Samples = Enumerable.Range(0, 1000).Select(i => new Sample { Timestamp = i }).ToList() }], Offsets = [0], BPMs = [120] };
            var files = new DelayedProjectFileService(); files.Release();
            var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) };
            var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: files);
            try
            {
                await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
                var timer = Stopwatch.StartNew();
                for (var i = 0; i < 100; i++) _ = ProjectDataSerializer.Serialize(project, default);
                var elapsed = timer.Elapsed.TotalMilliseconds;
                var before = GC.GetTotalAllocatedBytes(true); timer.Restart();
                for (var i = 0; i < 100; i++) vm.ToggleTracksCollapsedCommand.Execute(null);
                await vm.FlushSavesAsync();
                if (run > 0) { eager.Add(elapsed); coalesced.Add(timer.Elapsed.TotalMilliseconds); allocations.Add(GC.GetTotalAllocatedBytes(true) - before); }
                Assert.That(vm.SaveSnapshotCount, Is.EqualTo(1));
            }
            finally { await vm.DeactivateAsync(); }
        }
        eager.Sort(); coalesced.Sort(); allocations.Sort();
        TestContext.Out.WriteLine($"100 edits, 1,000 nodes: reference 100 serializations median={eager[2]:F3}ms; actual edit+flush median={coalesced[2]:F3}ms; process allocations median={allocations[2]} bytes; snapshots=1 (five runs after warmup).");
    }

    [Test]
    public async Task MeasureSearchBurstAndIndependentReadiness()
    {
        var inventory = new FolderSnapshot(Enumerable.Range(0, 10000).Select(i => new FolderEntry(i.ToString(), i.ToString(), default)));
        var filtering = new List<double>(); var burst = new List<double>(); var readiness = new List<double>(); var allocations = new List<long>();
        for (var run = 0; run < 6; run++)
        {
            var songs = new TaskCompletionSource<FolderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = new HomePageVM(new StubHomeFolderService { Songs = (_, token) => songs.Task.WaitAsync(token) },
                new StubAppSettingsService(), new StubInstallationDialogService(), new ImmediateUiScheduler(),
                new StubProjectFileService((_, _, _) => Task.FromResult("project.json")), new WeakReferenceMessenger(),
                searchDelay: (_, token) => delay.Task.WaitAsync(token));
            try
            {
                var timer = Stopwatch.StartNew(); await vm.LoadProjectsAsync(default);
                var ready = timer.Elapsed.TotalMilliseconds;
                Assert.That(vm.CanInteract, Is.True); Assert.That(songs.Task.IsCompleted, Is.False);
                songs.SetResult(inventory); await vm.LoadAsync(default); await vm.LoadInitialSongsAsync(default); vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
                timer.Restart();
                for (var i = 0; i < 20; i++) _ = inventory.FilterSongs(i.ToString(), default);
                var elapsed = timer.Elapsed.TotalMilliseconds;
                var before = GC.GetTotalAllocatedBytes(true); timer.Restart(); var pending = new List<Task>();
                for (var i = 0; i < 20; i++) { vm.SongSearchText = i.ToString(); pending.Add(vm.SearchSongsCommand.ExecutionTask!); }
                delay.SetResult(); await Task.WhenAll(pending);
                if (run > 0) { filtering.Add(elapsed); burst.Add(timer.Elapsed.TotalMilliseconds); readiness.Add(ready); allocations.Add(GC.GetTotalAllocatedBytes(true) - before); }
                Assert.That(vm.SongSearchFilterCount, Is.EqualTo(1));
            }
            finally { vm.Deactivate(); }
        }
        filtering.Sort(); burst.Sort(); readiness.Sort(); allocations.Sort();
        TestContext.Out.WriteLine($"20 queries, 10,000 entries: reference filters median={filtering[2]:F3}ms; debounced burst processing median={burst[2]:F3}ms; process allocations median={allocations[2]} bytes; filters=1. Projects-ready median={readiness[2]:F3}ms while Songs deliberately pending. Injected delay excludes the intentional 150ms quiet period (five runs after warmup).");
    }

    [Test]
    public void MeasureIndexedTimestampQueries()
    {
        Sample[] samples = Enumerable.Range(0,50000).Select(i => new Sample { Timestamp = i * 10 }).ToArray();
        var index = new SampleTimeIndex(); index.Update(samples,0);
        var reference = new List<double>(); var indexed = new List<double>();
        for (var run = 0; run < 6; run++)
        {
            var timer = Stopwatch.StartNew(); var count = 0;
            for (var i = 0; i < 100; i++) count += samples.Where(sample => sample.Timestamp >= 200000 && sample.Timestamp <= 201000).ToArray().Length;
            var scan = timer.Elapsed.TotalMilliseconds; timer.Restart(); var found = 0;
            for (var i = 0; i < 100; i++) found += index.Query(200000,201000).Count;
            Assert.That(found, Is.EqualTo(count));
            if (run > 0) { reference.Add(scan); indexed.Add(timer.Elapsed.TotalMilliseconds); }
        }
        reference.Sort(); indexed.Sort();
        TestContext.Out.WriteLine($"50,000 nodes, 100 queries: full-scan median={reference[2]:F3}ms; indexed median={indexed[2]:F3}ms (five runs after warmup).");
    }

    [TestCase(1000)]
    [TestCase(10000)]
    public async Task MeasureIncrementalMetadataRefresh(int count)
    {
        var root = Directory.CreateTempSubdirectory("hitbench-refresh-perf-");
        try
        {
            var app = root.CreateSubdirectory("app"); var osu = root.CreateSubdirectory("osu"); var songs = osu.CreateSubdirectory("Songs");
            for (var i = 0; i < count; i++)
            {
                var folder = songs.CreateSubdirectory(i.ToString());
                await File.WriteAllTextAsync(Path.Combine(folder.FullName,"map.osu"), $"[Metadata]\nTitle:Song {i}\n");
            }
            var cold = new List<double>(); var refresh = new List<double>();
            for (var run = 0; run < 6; run++)
            {
                File.Delete(Path.Combine(app.FullName,"data.json"));
                var service = new HomeFolderService(app.FullName); var timer = Stopwatch.StartNew();
                await service.LoadSongsAsync(Path.Combine(osu.FullName, "Songs"),default);
                var elapsed = timer.Elapsed.TotalMilliseconds;
                Assert.That(service.MetadataReadCount, Is.EqualTo(count));
                timer.Restart(); await service.ReloadSongsAsync(Path.Combine(osu.FullName, "Songs"),default);
                Assert.That(service.MetadataReadCount, Is.EqualTo(count), "Unchanged refresh must not parse metadata again.");
                if (run > 0) { cold.Add(elapsed); refresh.Add(timer.Elapsed.TotalMilliseconds); }
            }
            cold.Sort(); refresh.Sort();
            TestContext.Out.WriteLine($"{count} mapsets: cold metadata-cache median={cold[2]:F3}ms; unchanged refresh median={refresh[2]:F3}ms; metadata reparses=0 (five runs after warmup).");
        }
        finally { root.Delete(true); }
    }
}
