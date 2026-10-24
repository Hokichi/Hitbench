using Onset.Services.Dialogs;

namespace Onset.Test.Presentation;

public sealed class StubSampleFileDialogService(IReadOnlyList<string> selectedPaths) : ISampleFileDialogService
{
    public StubSampleFileDialogService(string? selectedPath) : this(selectedPath is null ? Array.Empty<string>() : new[] { selectedPath }) { }
    public Func<CancellationToken, Task<IReadOnlyList<string>>>? Selection { get; set; }
    public string? InitialDirectory { get; private set; }
    public Onset.Resources.DataModels.SampleSelectionMode Mode { get; private set; }
    public Onset.Resources.DataModels.SamplePickerContext? Context { get; private set; }
    public Task<IReadOnlyList<string>> SelectAsync(Onset.Resources.DataModels.SamplePickerContext context, CancellationToken token)
    {
        Context = context;
        return SelectAsync(context.InitialDirectory ?? context.SongDirectory, token, context.Mode);
    }
    public Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token, Onset.Resources.DataModels.SampleSelectionMode mode=Onset.Resources.DataModels.SampleSelectionMode.Multiple)
    {
        InitialDirectory = initialDirectory;
        Mode=mode;
        return Selection?.Invoke(token) ?? Task.FromResult(selectedPaths);
    }
}
