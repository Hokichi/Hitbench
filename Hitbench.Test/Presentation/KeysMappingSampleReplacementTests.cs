using Hitbench.Resources.DataModels;

namespace Hitbench.Test.Presentation;

public sealed class KeysMappingSampleReplacementTests
{
    [Test] public async Task LoadsCapturedSelectedKeyWithoutCommittingDirtyDraft()
    {
        var picker = new StubSampleFileDialogService("kick.wav");
        var preview = new StubSamplePreviewService();
        var files = new StubSampleFileService(["Piano/C5.wav"]);
        var imports = 0;
        files.Import = (_, _, _) => throw new InvalidOperationException("Draft must not be committed");
        files.ImportKey = (_, path, folder, key, _) =>
        {
            imports++;
            Assert.That(preview.Playing, Is.False);
            Assert.That(path, Is.EqualTo("kick.wav")); Assert.That(folder, Is.EqualTo("Piano")); Assert.That(key, Is.EqualTo("C5"));
            return Task.FromResult("Piano/C5.wav");
        };
        await using var c = await OHsKeysMappingTestContext.CreateAsync(files: files, preview: preview, picker: picker);
        Assert.That(c.VM.LoadSelectedCommand.CanExecute(null), Is.False);
        await c.VM.SelectKeyAsync("C5"); await c.VM.PlaySelectedAsync(default);
        c.VM.SelectedEntry!.DraftFileName = "../invalid.wav"; c.VM.SelectedEntry.Error = "old error";
        await c.VM.LoadSelectedCommand.ExecuteAsync(null);
        Assert.That(picker.Mode, Is.EqualTo(SampleSelectionMode.Single));
        Assert.That(picker.Context!.ProjectPath, Is.EqualTo(c.Snapshot.Context.ProjectPath));
        Assert.That(c.Track.KeyFiles["C5"], Is.EqualTo("Piano/C5.wav"));
        Assert.That(c.VM.SelectedEntry.DraftFileName, Is.EqualTo("Piano/C5.wav"));
        Assert.That(c.VM.SelectedEntry.Error, Is.Null); Assert.That(imports, Is.EqualTo(1));
    }
    [TestCase(false)][TestCase(true)] public async Task CancelOrFailurePreservesMappingAndDraft(bool fail)
    {
        var files = new StubSampleFileService(["Piano/C5.wav"]) { ImportKey = (_, _, _, _, _) => throw new IOException("copy failed") };
        var picker = new StubSampleFileDialogService(fail ? "kick.ogg" : null);
        await using var c = await OHsKeysMappingTestContext.CreateAsync(files: files, picker: picker);
        await c.VM.SelectKeyAsync("C5"); c.VM.SelectedEntry!.DraftFileName = "../invalid.wav";
        await c.VM.LoadSelectedCommand.ExecuteAsync(null);
        Assert.That(c.Track.KeyFiles["C5"], Is.EqualTo("Piano/C5.wav"));
        Assert.That(c.VM.SelectedEntry.DraftFileName, Is.EqualTo("../invalid.wav"));
        Assert.That(c.VM.CanEditSelectedMapping, Is.True);
        if (fail) Assert.That(c.VM.ErrorMessage, Does.Contain("copy failed"));
    }
    [Test] public async Task PickerBlocksEditsAndTeardownDrainsCancelledWork()
    {
        var release = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var picker = new StubSampleFileDialogService((string?)null) { Selection = _ => release.Task };
        var imports = 0;
        var files = new StubSampleFileService([]) { ImportKey = (_, _, _, _, _) => { imports++; return Task.FromResult("Piano/C5.wav"); } };
        await using var c = await OHsKeysMappingTestContext.CreateAsync(files: files, picker: picker);
        await c.VM.SelectKeyAsync("C5");
        var operation = c.VM.LoadSelectedCommand.ExecuteAsync(null);
        Assert.That(c.VM.LoadSelectedCommand.CanExecute(null), Is.False); Assert.That(c.VM.CanClose, Is.False);
        Assert.That(await c.VM.SelectKeyAsync("D5"), Is.False);
        var closing = c.VM.DeactivateAsync(); Assert.That(closing.IsCompleted, Is.False);
        release.SetResult(["kick.wav"]); await Task.WhenAll(operation, closing);
        Assert.That(imports, Is.Zero); Assert.That(c.VM.LoadSelectedCommand.CanExecute(null), Is.False);
    }
    [Test] public async Task CommittedReplacementPublishesDuringTeardownDespiteLateCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var files = new StubSampleFileService(["Piano/D5.ogg"]) { ImportKey = (_, _, _, _, _) => { entered.SetResult(); return release.Task; } };
        await using var c = await OHsKeysMappingTestContext.CreateAsync(files: files, picker: new StubSampleFileDialogService("kick.ogg"));
        await c.VM.SelectKeyAsync("D5");
        var loading = c.VM.LoadSelectedCommand.ExecuteAsync(null); await entered.Task;
        var closing = c.VM.DeactivateAsync();
        release.SetResult("Piano/D5.ogg"); await Task.WhenAll(loading, closing);
        Assert.That(c.Track.KeyFiles["D5"], Is.EqualTo("Piano/D5.ogg"));
    }
    [Test] public async Task MultiplePickerResultsAreRejected()
    {
        await using var c = await OHsKeysMappingTestContext.CreateAsync(picker: new StubSampleFileDialogService(["a.wav", "b.wav"]));
        await c.VM.SelectKeyAsync("C5"); await c.VM.LoadSelectedCommand.ExecuteAsync(null);
        Assert.That(c.VM.ErrorMessage, Does.Contain("Select one file"));
        Assert.That(c.Track.KeyFiles["C5"], Is.EqualTo("Piano/C5.wav"));
    }
}
