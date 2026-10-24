using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

public sealed class StubInstrumentalTrackDialogService : IInstrumentalTrackDialogService
{
    public Func<InstrumentalTrackSnapshot, CancellationToken, Task>? Mapping { get; set; }
    public Func<InstrumentalEditorContext, CancellationToken, Task>? Arrangement { get; set; }
    public Task ShowKeysMappingAsync(InstrumentalTrackSnapshot snapshot, CancellationToken token) => Mapping?.Invoke(snapshot, token) ?? Task.CompletedTask;
    public Task ShowArrangementAsync(InstrumentalEditorContext context, CancellationToken token) => Arrangement?.Invoke(context, token) ?? Task.CompletedTask;
}
