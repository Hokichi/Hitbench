using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

public class OHsHitNormalWaveformTests
{
    private static OHsHitNormalPopupVM Create(StubSampleBrowserService reader) => new(new(),
        [new("a.wav", "a.wav"), new("b.wav", "b.wav"), new("c.wav", "c.wav")],
        Path.Combine(Path.GetTempPath(), "hitnormal-waveforms", "project.json"), "", reader, new ImmediateUiScheduler());

    [Test]
    public async Task ReadsAreLazyCoalescedAndBoundedAndAssignedSlotsShareData()
    {
        var reads = 0;
        var release = new TaskCompletionSource<WaveformData>();
        var expected = new WaveformData(TimeSpan.FromSeconds(1), [.5f]);
        var reader = new StubSampleBrowserService { Waveform = (_, _) => { reads++; return release.Task; } };
        var vm = Create(reader);
        Assert.That(reads, Is.Zero);
        await vm.InitializeAsync(default);
        var rows = vm.CatalogSamples.Where(s => s.Path.Length > 0).ToArray();
        var tasks = rows.Select(vm.LoadWaveformAsync).ToArray();
        var duplicate = vm.LoadWaveformAsync(rows[0]);
        Assert.That(reads, Is.EqualTo(2));
        try
        {
            release.SetResult(expected);
            await Task.WhenAll(tasks.Append(duplicate));
            Assert.That(reads, Is.EqualTo(3));
            vm.AssignSampleCommand.Execute(rows[0]);
            vm.SelectedSlot = vm.Slots[1];
            vm.AssignSampleCommand.Execute(rows[0]);
            Assert.That(vm.Slots[0].Sample.Waveform, Is.SameAs(expected));
            Assert.That(vm.Slots[1].Sample.Waveform, Is.SameAs(expected));
            await vm.LoadWaveformAsync(rows[0]);
            Assert.That(reads, Is.EqualTo(3));
        }
        finally { release.TrySetResult(expected); await Task.WhenAll(tasks); await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ClosingCancelsQueuedReadsAndIgnoresDecoderIgnoringCancellation()
    {
        var release = new TaskCompletionSource<WaveformData>();
        var tokens = new List<CancellationToken>();
        var vm = Create(new() { Waveform = (_, token) => { tokens.Add(token); return release.Task; } });
        await vm.InitializeAsync(default);
        var rows = vm.CatalogSamples.Where(s => s.Path.Length > 0).ToArray();
        var loads = rows.Select(vm.LoadWaveformAsync).ToArray();
        var closing = vm.DeactivateAsync();
        try
        {
            Assert.That(tokens, Has.Count.EqualTo(2));
            Assert.That(tokens.All(t => t.IsCancellationRequested), Is.True);
            Assert.That(closing.IsCompleted, Is.False);
        }
        finally { release.TrySetResult(new(TimeSpan.FromSeconds(1), [.3f])); await Task.WhenAll(loads.Append(closing)); }
        Assert.That(tokens, Has.Count.EqualTo(2));
        Assert.That(rows.All(s => s.Waveform is null), Is.True);
        Assert.That(vm.AssignSampleCommand.CanExecute(rows[0]), Is.False);
        await vm.DeactivateAsync();
    }

    [Test]
    public async Task LateOldAssignmentDoesNotReplaceNewWaveformAndFailureDoesNotDisableAssignment()
    {
        var release = new TaskCompletionSource<WaveformData>();
        var fresh = new WaveformData(TimeSpan.FromSeconds(1), [.9f]);
        var vm = Create(new() { Waveform = (path, _) => Path.GetFileName(path) switch
        {
            "a.wav" => release.Task,
            "b.wav" => Task.FromResult(fresh),
            _ => Task.FromException<WaveformData>(new IOException("Unreadable sample"))
        } });
        await vm.InitializeAsync(default);
        var a = vm.CatalogSamples.Single(s => s.Path == "a.wav");
        var b = vm.CatalogSamples.Single(s => s.Path == "b.wav");
        vm.AssignSampleCommand.Execute(a);
        var old = vm.LoadWaveformAsync(a);
        try
        {
            vm.AssignSampleCommand.Execute(b);
            await vm.LoadWaveformAsync(b);
            release.SetResult(new(TimeSpan.FromSeconds(1), [.1f]));
            await old;
            Assert.That(vm.Slots[0].Sample.Waveform, Is.SameAs(fresh));
            var bad = vm.CatalogSamples.Single(s => s.Path == "c.wav");
            await vm.LoadWaveformAsync(bad);
            Assert.That(bad.Waveform, Is.Null);
            Assert.That(vm.AssignSampleCommand.CanExecute(bad), Is.True);
        }
        finally { release.TrySetResult(fresh); await old; await vm.DeactivateAsync(); }
    }
}
