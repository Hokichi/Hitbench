using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
namespace osuHitsounder.Test.Presentation;

public class OHsExportPopupVMTests
{
    [Test]
    public void InvalidNameReportsAndClearsFieldValidationErrors()
    {
        var vm = new OHsExportPopupVM(Request(), new StubOHsExportService(), new StubOHsExportDirectoryLauncher(), new WeakReferenceMessenger());
        vm.DiffName = "../bad";
        Assert.That(vm.GetErrors(nameof(vm.DiffName)), Is.Not.Empty);
        vm.DiffName = "Hitsounds";
        Assert.That(vm.GetErrors(nameof(vm.DiffName)), Is.Empty);
    }
    private static OHsExportRequest Request() => new(Guid.NewGuid(), "project.json", "map.osu", "song.wav", "{}", new("Hitsounds", 1, ".ogg", ".ogg", false));
    [Test]
    public async Task DefaultsAndInvalidNumericDraftGateExportAndChosenOptionsReachService()
    {
        var service = new StubOHsExportService(); var launcher = new StubOHsExportDirectoryLauncher();
        var vm = new OHsExportPopupVM(Request(), service, launcher, new WeakReferenceMessenger());
        Assert.That((vm.DiffName, vm.StartingIndex, vm.SingleFormat, vm.MixedFormat, vm.ShowFilesAfterExport), Is.EqualTo(("Hitsounds", 1, ".ogg", ".ogg", false)));
        Assert.That(vm.Formats, Is.EqualTo(new[] { ".ogg", ".wav", ".mp3" }));
        vm.DiffName = "../bad"; Assert.That(vm.CanExport, Is.False); vm.DiffName = "Custom";
        vm.SetIndexInputError(true); Assert.That(vm.ExportCommand.CanExecute(null), Is.False);
        vm.SetIndexInputError(false); vm.StartingIndex = 999; vm.SingleFormat = ".wav"; vm.MixedFormat = ".mp3";
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.That(service.LastRequest!.Options, Is.EqualTo(new OHsExportOptions("Custom", 999, ".wav", ".mp3", false)));
        Assert.That(launcher.Calls, Is.Zero); await vm.DeactivateAsync();
    }
    [Test]
    public async Task ClosingDefersUntilOwnedOperationSettlesAndSuppressesDirectoryLaunch()
    {
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource<string>(); CancellationToken observed = default;
        var service = new StubOHsExportService { Run = (_, token) => { observed = token; started.SetResult(); return release.Task; } };
        var launcher = new StubOHsExportDirectoryLauncher(); var vm = new OHsExportPopupVM(Request(), service, launcher, new WeakReferenceMessenger()) { ShowFilesAfterExport = true };
        var completed = false; vm.Completed += _ => completed = true;
        var export = vm.ExportCommand.ExecuteAsync(null); await started.Task;
        await vm.ExportCommand.ExecuteAsync(null); Assert.That(service.Calls, Is.EqualTo(1));
        vm.RequestClose(); var deactivate = vm.DeactivateAsync();
        Assert.That(observed.IsCancellationRequested, Is.True); Assert.That(deactivate.IsCompleted, Is.False); Assert.That(completed, Is.False);
        release.SetResult("Export"); await export; await deactivate;
        Assert.That(completed, Is.True); Assert.That(launcher.Calls, Is.Zero);
    }
    [Test]
    public async Task FailureRestoresEditingAndSuccessfulPublishLaunchesOnce()
    {
        var service = new StubOHsExportService { Run = (_, _) => throw new IOException("Cannot export") };
        var launcher = new StubOHsExportDirectoryLauncher(); var vm = new OHsExportPopupVM(Request(), service, launcher, new WeakReferenceMessenger()) { ShowFilesAfterExport = true };
        await vm.ExportCommand.ExecuteAsync(null); Assert.That(vm.CanExport, Is.True); Assert.That(vm.IsBusy, Is.False);
        service.Run = (_, _) => Task.FromResult("Export");
        await vm.ExportCommand.ExecuteAsync(null); Assert.That(launcher.Calls, Is.EqualTo(1)); await vm.DeactivateAsync();
    }
}
