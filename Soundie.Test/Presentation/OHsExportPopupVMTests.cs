using CommunityToolkit.Mvvm.Messaging;
using Soundie.Resources.DataModels;
using Soundie.ViewModels.Popups;
namespace Soundie.Test.Presentation;

public class OHsExportPopupVMTests
{
    [Test]
    public async Task DestinationAndOverwriteAreGatedAndSubmitted()
    {
        var service=new StubOHsExportService(); var launcher=new StubOHsExportDirectoryLauncher();
        var vm=new OHsExportPopupVM(Request(),service,launcher,new WeakReferenceMessenger());
        Assert.That(vm.Destination,Is.EqualTo(OHsExportDestination.Project));
        Assert.That(vm.OverwriteEverything,Is.False);
        vm.OverwriteEverything=true; vm.Destination=OHsExportDestination.Beatmap;
        Assert.That(vm.CanOverwriteEverything,Is.False);
        vm.Destination=OHsExportDestination.Project; Assert.That(vm.CanOverwriteEverything,Is.True);
        vm.IsBusy=true; Assert.That(vm.CanOverwriteEverything,Is.False); vm.IsBusy=false;
        vm.Destination=OHsExportDestination.Beatmap;
        string? opened=null; launcher.Run=(path,_)=>{opened=path;return Task.CompletedTask;};
        service.Run=(_,_)=>Task.FromResult("beatmap-folder");vm.ShowFilesAfterExport=true;
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.That(service.LastRequest!.Options.Destination,Is.EqualTo(OHsExportDestination.Beatmap));
        Assert.That(service.LastRequest.Options.OverwriteEverything,Is.False);
        Assert.That(opened,Is.EqualTo("beatmap-folder"));
    }
    [Test]
    public void ProgressIsMonotonicRetrySafeAndReachesHundredOnlyAfterPublication()
    {
        var saved = SynchronizationContext.Current;
        var context = new QueuedSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var release = new TaskCompletionSource<string>();
            var service = new StubOHsExportService { Run = (_, _) => release.Task };
            var vm = new OHsExportPopupVM(Request(), service, new StubOHsExportDirectoryLauncher(), new WeakReferenceMessenger());
            var first = vm.ExportCommand.ExecuteAsync(null);
            var old = service.LastProgress!;
            old.Report(new(40, "Decoding")); context.Drain();
            old.Report(new(20, "Old phase")); context.Drain();
            Assert.That(vm.ExportProgress, Is.EqualTo(40));
            release.SetException(new IOException("Cannot export")); context.Drain();
            Assert.That(first.IsCompletedSuccessfully, Is.True);
            Assert.That(vm.ExportToolTip, Is.EqualTo("Cannot export"));
            release = new();
            var second = vm.ExportCommand.ExecuteAsync(null);
            Assert.That(vm.ExportProgress, Is.Zero);
            old.Report(new(90, "Old attempt")); context.Drain();
            Assert.That(vm.ExportProgress, Is.Zero);
            service.LastProgress!.Report(new(100, "Premature success")); context.Drain();
            Assert.That(vm.ExportProgress, Is.LessThan(100));
            release.SetResult("Export"); context.Drain();
            Assert.That(second.IsCompletedSuccessfully, Is.True);
            Assert.That(vm.ExportProgress, Is.EqualTo(100));
            service.LastProgress.Report(new(10, "Late")); context.Drain();
            Assert.That(vm.ExportProgress, Is.EqualTo(100));
        }
        finally { SynchronizationContext.SetSynchronizationContext(saved); }
    }

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
    [Test]
    public async Task DirectoryLaunchFailureKeepsPublicationSuccessfulAndReportsError()
    {
        var messenger = new WeakReferenceMessenger(); var recipient = new object();
        var messages = new List<Soundie.Resources.Messages.OHsExportResultMessage>();
        messenger.Register<Soundie.Resources.Messages.OHsExportResultMessage>(recipient, (_, message) => messages.Add(message));
        var launcher = new StubOHsExportDirectoryLauncher { Run = (_, _) => throw new IOException("Explorer unavailable") };
        var vm = new OHsExportPopupVM(Request(), new StubOHsExportService(), launcher, messenger) { ShowFilesAfterExport = true };
        bool? completion = null; vm.Completed += accepted => completion = accepted;
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.That(completion, Is.True); Assert.That(messages.Last().IsPublished, Is.True);
        Assert.That(messages.Last().Error, Is.EqualTo("Explorer unavailable"));
        Assert.That(vm.IsBusy, Is.False); await vm.DeactivateAsync(); GC.KeepAlive(recipient);
    }
}
