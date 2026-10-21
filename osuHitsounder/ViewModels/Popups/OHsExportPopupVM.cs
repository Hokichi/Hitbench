using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.ComponentModel.DataAnnotations;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
namespace osuHitsounder.ViewModels.Popups;

public sealed partial class OHsExportPopupVM : ObservableValidator
{
    private readonly OHsExportRequest request;
    private readonly IOHsExportService exporter;
    private readonly IOHsExportDirectoryLauncher launcher;
    private readonly IMessenger messenger;
    private CancellationTokenSource? cancellation;
    private Task active = Task.CompletedTask;
    private bool indexInputError, closeRequested, completed;
    private long progressAttempt;
    public OHsExportPopupVM(OHsExportRequest request, IOHsExportService exporter, IOHsExportDirectoryLauncher directoryLauncher, IMessenger messenger)
    {
        this.request = request; this.exporter = exporter; launcher = directoryLauncher; this.messenger = messenger;
        diffName = request.Options.DiffName; startingIndex = request.Options.StartingIndex;
        singleFormat = request.Options.SingleFormat; mixedFormat = request.Options.MixedFormat; showFilesAfterExport = request.Options.ShowFilesAfterExport;
        ExportCommand = new AsyncRelayCommand(RunAsync, () => CanExport); CancelCommand = new RelayCommand(RequestClose);
    }
    [ObservableProperty, NotifyDataErrorInfo]
    [CustomValidation(typeof(SampleFileNames), nameof(SampleFileNames.ValidateStem))]
    private string diffName;
    [ObservableProperty] private int startingIndex;
    [ObservableProperty] private string singleFormat;
    [ObservableProperty] private string mixedFormat;
    [ObservableProperty] private bool showFilesAfterExport;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double exportProgress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ExportToolTip))] private string progressPhase = "Preparing export";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ExportToolTip))] private string? exportError;
    public string ExportToolTip => ExportError ?? (IsBusy ? ProgressPhase : "Export");
    public IReadOnlyList<string> Formats { get; } = Array.AsReadOnly(new[] { ".ogg", ".wav", ".mp3" });
    public string? NameError => SampleFileNames.GetStemError(DiffName);
    public bool HasNameError => NameError is not null;
    public bool CanEditOptions => !IsBusy && !closeRequested && !completed;
    public bool CanExport => CanEditOptions && !indexInputError && NameError is null && StartingIndex is >= 1 and <= 999 && Formats.Contains(SingleFormat) && Formats.Contains(MixedFormat);
    public IAsyncRelayCommand ExportCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public event Action<bool>? Completed;
    partial void OnDiffNameChanged(string value) { OnPropertyChanged(nameof(NameError)); OnPropertyChanged(nameof(HasNameError)); NotifyState(); }
    partial void OnStartingIndexChanged(int value) => NotifyState();
    partial void OnSingleFormatChanged(string value) => NotifyState();
    partial void OnMixedFormatChanged(string value) => NotifyState();
    partial void OnIsBusyChanged(bool value) { NotifyState(); OnPropertyChanged(nameof(ExportToolTip)); }
    private void NotifyState()
    { OnPropertyChanged(nameof(CanEditOptions)); OnPropertyChanged(nameof(CanExport)); ExportCommand.NotifyCanExecuteChanged(); }
    public void SetIndexInputError(bool value) { indexInputError = value; NotifyState(); }
    private Task RunAsync()
    {
        if (!CanExport) return Task.CompletedTask;
        active = ExportCoreAsync(); return active;
    }
    private async Task ExportCoreAsync()
    {
        cancellation = new(); IsBusy = true; var published = false;
        var attempt = ++progressAttempt;
        ExportProgress = 0; ExportError = null; ProgressPhase = "Preparing export";
        var progress = new Progress<OHsExportProgress>(value =>
        {
            if (attempt != progressAttempt || !IsBusy || completed || closeRequested || cancellation?.IsCancellationRequested != false ||
                !double.IsFinite(value.Percent) || value.Percent < ExportProgress) return;
            ExportProgress = Math.Clamp(value.Percent, 0, 99);
            ProgressPhase = value.Phase;
        });
        try
        {
            var options = new OHsExportOptions(DiffName, StartingIndex, SingleFormat, MixedFormat, ShowFilesAfterExport);
            var directory = await exporter.ExportAsync(request with { Options = options }, cancellation.Token, progress);
            published = true;
            ExportProgress = 100;
            messenger.Send(new OHsExportResultMessage(request.OperationId, request.ProjectPath, true, null));
            if (options.ShowFilesAfterExport && !closeRequested && !cancellation.IsCancellationRequested)
                await launcher.OpenAsync(directory, cancellation.Token);
            Complete(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { if (published) Complete(true); }
        catch (Exception error)
        {
            ExportError = error.Message;
            messenger.Send(new OHsExportResultMessage(request.OperationId, request.ProjectPath, published, error.Message));
            if (published) Complete(true);
        }
        finally
        {
            IsBusy = false; cancellation.Dispose(); cancellation = null;
            if (closeRequested) Complete(published);
        }
    }
    private void Complete(bool accepted)
    { if (completed) return; completed = true; NotifyState(); Completed?.Invoke(accepted); }
    public void RequestClose()
    {
        closeRequested = true; NotifyState(); cancellation?.Cancel();
        if (!IsBusy) Complete(false);
    }
    public async Task DeactivateAsync() { RequestClose(); await active; }
}
