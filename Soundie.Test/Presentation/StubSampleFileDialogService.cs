using Soundie.Services.Dialogs;

namespace Soundie.Test.Presentation;

public sealed class StubSampleFileDialogService(IReadOnlyList<string> selectedPaths) : ISampleFileDialogService
{
    public StubSampleFileDialogService(string? selectedPath) : this(selectedPath is null ? Array.Empty<string>() : new[] { selectedPath }) { }
    public Func<CancellationToken, Task<IReadOnlyList<string>>>? Selection { get; set; }
    public string? InitialDirectory { get; private set; }
    public Soundie.Resources.DataModels.SampleSelectionMode Mode { get; private set; }
    public Soundie.Resources.DataModels.SamplePickerContext? Context { get; private set; }
    public Task<IReadOnlyList<string>> SelectAsync(Soundie.Resources.DataModels.SamplePickerContext context, CancellationToken token)
    {
        Context = context;
        return SelectAsync(context.InitialDirectory ?? context.SongDirectory, token, context.Mode);
    }
    public Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token, Soundie.Resources.DataModels.SampleSelectionMode mode=Soundie.Resources.DataModels.SampleSelectionMode.Multiple)
    {
        InitialDirectory = initialDirectory;
        Mode=mode;
        return Selection?.Invoke(token) ?? Task.FromResult(selectedPaths);
    }
}
