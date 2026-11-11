using Hitbench.Test;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public sealed class WorkstationLastChangedTests
{
    private static readonly DateTimeOffset Instant = DateTimeOffset.Parse("2026-10-18T01:00:00Z");

    private static async Task<WorkstationPageVM> Ready(IProjectFileService files, ManualTimeProvider clock,
        StubSampleFileService? samples = null, StubSampleFileDialogService? dialog = null, CancellationToken token = default)
    {
        var project = new ProjectData { Tracks = [new Track()], UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new()] };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), samples, dialog, files, timeProvider: clock);
        await vm.LoadAsync("project.json", token);
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        return vm;
    }

    [Test, Repeat(20)]
    public async Task SnapshotCreationWaitsForFlushOrQuietPeriod()
    {
        var files = new DelayedProjectFileService(); files.Release();
        var clock = new ManualTimeProvider(); clock.SetUtcNow(Instant);
        var vm = await Ready(files, clock);
        try
        {
            for (var i = 0; i < 100; i++) vm.ToggleTracksCollapsedCommand.Execute(null);
            Assert.That(vm.SaveSnapshotCount, Is.Zero);
            await vm.FlushSavesAsync();
            Assert.That(vm.SaveSnapshotCount, Is.EqualTo(1));
        }
        finally { await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task BurstKeepsOnlyLatestPendingSnapshotAndFlushDrainsNewWork()
    {
        var files = new DelayedProjectFileService();
        var clock = new ManualTimeProvider(); clock.SetUtcNow(Instant);
        var vm = await Ready(files, clock);
        try
        {
            vm.ToggleTracksCollapsedCommand.Execute(null);
            var flush = vm.FlushSavesAsync();
            for (var i = 1; i <= 100; i++)
            { clock.SetUtcNow(Instant.AddSeconds(i)); vm.ToggleTracksCollapsedCommand.Execute(null); }
            files.Release(); await flush;
            Assert.That(files.Writes, Is.EqualTo(2));
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).LastChanged, Is.EqualTo(Instant.AddSeconds(100)));
            Assert.That(vm.Project!.LastChanged, Is.EqualTo(Instant.AddSeconds(100)));
            Assert.That(vm.HasPendingSaves, Is.False);
        }
        finally { files.Release(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task PartiallyCancelledImportStillPersistsSuccessfulCopies()
    {
        using var cancellation = new CancellationTokenSource();
        var files = new DelayedProjectFileService(); files.Release();
        var clock = new ManualTimeProvider(); clock.SetUtcNow(Instant);
        var copied = 0;
        var samples = new StubSampleFileService([]) { Import = (_, _, token) =>
        {
            if (++copied == 1) return Task.FromResult("first.wav");
            cancellation.Cancel();
            return Task.FromCanceled<string>(token);
        } };
        var vm = await Ready(files, clock, samples, new StubSampleFileDialogService(["first.wav", "second.wav"]), cancellation.Token);
        try
        {
            await vm.ImportSamplesAsync(); await vm.FlushSavesAsync();
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).LastChanged, Is.EqualTo(Instant));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task PendingZoomPersistsOnReturnAndNoOpPreferenceDoesNotSave()
    {
        var files = new DelayedProjectFileService(); files.Release();
        var clock = new ManualTimeProvider(); clock.SetUtcNow(Instant);
        var vm = await Ready(files, clock);
        try
        {
            vm.SetTrackCollapsed(vm.Tracks.Single(), false);
            Assert.That(files.Writes, Is.Zero);
            vm.ZoomInCommand.Execute(null);
            Assert.That(await vm.PrepareToReturnAsync(default), Is.True);
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).LastChanged, Is.EqualTo(Instant));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ReturningWithoutEditsPreservesTimestamp()
    {
        var files = new DelayedProjectFileService(); files.Release();
        var clock = new ManualTimeProvider(); clock.SetUtcNow(Instant);
        var vm = await Ready(files, clock);
        try
        {
            Assert.That(await vm.PrepareToReturnAsync(default), Is.True);
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).LastChanged, Is.EqualTo(default(DateTimeOffset)));
            Assert.That(vm.Project!.LastChanged, Is.EqualTo(default(DateTimeOffset)));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task TimestampCommitsOnlyAfterSuccessfulWriteAndPreservesQueueOrder()
    {
        var files = new DelayedProjectFileService();
        var clock = new ManualTimeProvider(); clock.SetUtcNow(Instant);
        var vm = await Ready(files, clock);
        try
        {
            vm.ToggleTracksCollapsedCommand.Execute(null);
            Assert.That(vm.Project!.LastChanged, Is.EqualTo(default(DateTimeOffset)));
            clock.SetUtcNow(Instant.AddMinutes(1));
            vm.ToggleTracksCollapsedCommand.Execute(null);
            files.Release(); await vm.FlushSavesAsync();
            Assert.That(vm.Project.LastChanged, Is.EqualTo(Instant.AddMinutes(1)));
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).LastChanged, Is.EqualTo(Instant.AddMinutes(1)));
            clock.SetUtcNow(Instant.AddHours(-1));
            vm.ToggleTracksCollapsedCommand.Execute(null); await vm.FlushSavesAsync();
            Assert.That(vm.Project.LastChanged, Is.EqualTo(Instant.AddMinutes(1)));
        }
        finally { files.Release(); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task FailedSaveDoesNotCommitAndRetryKeepsOriginalChangeTime()
    {
        var files = new FailOnceProjectFileService();
        var clock = new ManualTimeProvider(); clock.SetUtcNow(Instant);
        var vm = await Ready(files, clock);
        try
        {
            vm.ToggleTracksCollapsedCommand.Execute(null);
            Assert.ThrowsAsync<IOException>(() => vm.FlushSavesAsync());
            Assert.That(vm.Project!.LastChanged, Is.EqualTo(default(DateTimeOffset)));
            clock.SetUtcNow(Instant.AddHours(1));
            vm.RetrySaveCommand.Execute(null); await vm.FlushSavesAsync();
            Assert.That(ProjectDataSerializer.Deserialize(files.SavedJson!).LastChanged, Is.EqualTo(Instant));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FileOnlyImportStampsEvenWhenChoiceRefreshFails(bool refreshFails)
    {
        var files = new DelayedProjectFileService(); files.Release();
        var clock = new ManualTimeProvider(); clock.SetUtcNow(Instant);
        var samples = new StubSampleFileService([]) { Import = (_, _, _) => Task.FromResult("new.wav") };
        var vm = await Ready(files, clock, samples, new StubSampleFileDialogService(["new.wav"]));
        try
        {
            if (refreshFails) samples.List = (_, _) => throw new IOException("Refresh failed");
            await vm.ImportSamplesAsync(); await vm.FlushSavesAsync();
            Assert.That(ProjectDataSerializer.Deserialize(files.LatestJson!).LastChanged, Is.EqualTo(Instant));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
