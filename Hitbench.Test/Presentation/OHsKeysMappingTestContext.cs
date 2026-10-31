using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;
using Hitbench.Services.Dialogs;
using Hitbench.ViewModels.Popups;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public sealed class OHsKeysMappingTestContext : IAsyncDisposable
{
    public InstrumentalTrack Track { get; } = new() { Name = "Piano", FolderName = "Piano", KeyFiles = new() { ["C5"] = "Piano/C5.wav" }, Notes = [new() { Key = "C5", Timestamp = 125 }] };
    public InstrumentalTrack Other { get; } = new() { Name = "Other", KeyFiles = new() { ["D5"] = "other.wav" } };
    public WeakReferenceMessenger Messenger { get; } = new();
    public StubSamplePreviewService Preview { get; }
    public PlaybackVolumeSettings Volumes { get; } = new();
    public StubWorkstationAudioService Audio { get; } = new();
    public WorkstationPageVM Owner { get; }
    public KeysMappingDrawerVM VM { get; private set; } = null!;
    public InstrumentalTrackSnapshot Snapshot { get; private set; } = null!;
    public int Presentations { get; private set; }
    private Task workflow = Task.CompletedTask;
    private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private OHsKeysMappingTestContext(Func<string, CancellationToken, Task<bool>>? confirm, StubSamplePreviewService? preview, ISampleFileService? files, IProjectFileService? projectFiles, ISampleFileDialogService? picker)
    {
        Preview = preview ?? new();
        files ??= new StubSampleFileService(["Piano/C5.wav", "Piano/new.wav", "other.wav"]);
        Messenger.Register<InstrumentalTrackChangedMessage>(this, (_, message) => Snapshot = message.Snapshot);
        var dialogs = new StubInstrumentalTrackDialogService();
        Owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [Track, Other], Offsets = [0], BPMs = [120], ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(Audio, Task.Delay), new ImmediateUiScheduler(), sampleFiles: files, messenger: Messenger, instrumentDialogs: dialogs, projectFiles: projectFiles);
        dialogs.Mapping = async (snapshot, token) =>
        {
            Presentations++;
            Snapshot = snapshot;
            VM = new(snapshot, Messenger, new ImmediateUiScheduler(), files, picker ?? new StubSampleFileDialogService((string?)null), preview: Presentations == 1 ? Preview : new StubSamplePreviewService(), playbackVolumes: Volumes, confirmClearMappings: confirm ?? ((_, _) => Task.FromResult(true)));
            opened.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { await VM.DeactivateAsync(); }
        };
    }

    public static async Task<OHsKeysMappingTestContext> CreateAsync(Func<string, CancellationToken, Task<bool>>? confirm = null, StubSamplePreviewService? preview = null, ISampleFileService? files = null, IProjectFileService? projectFiles = null, string projectPath = "project.json", ISampleFileDialogService? picker = null)
    {
        var result = new OHsKeysMappingTestContext(confirm, preview, files, projectFiles, picker);
        await result.Owner.LoadAsync(projectPath, default);
        result.Owner.AcknowledgePresentationReady(result.Owner.LoadGeneration);
        result.workflow = result.Owner.KeysMappingCommand.ExecuteAsync(result.Track);
        await result.opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        try { await Owner.SettleInstrumentalEditorAsync(); await workflow; }
        finally { Messenger.UnregisterAll(this); await Owner.DeactivateAsync(); }
    }
}
