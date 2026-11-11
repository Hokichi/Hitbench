using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

public sealed class StubOHsMidiImportDialogService : IOHsMidiImportDialogService
{
    public string? Path { get; set; } = "song.mid";
    public OHsMidiImportMode? Mode { get; set; } = OHsMidiImportMode.AsIs;
    public bool AcceptExclusions { get; set; } = true;
    public int TimingChoiceCalls { get; private set; }
    public int ExclusionCalls { get; private set; }
    public Func<CancellationToken, Task<OHsMidiImportMode?>>? Choose { get; set; }
    public List<(string Title, string Message)> Errors { get; } = [];
    public Task<string?> PickFileAsync(CancellationToken token) => Task.FromResult(Path);
    public Task<OHsMidiImportMode?> ChooseTimingAsync(OHsMidiImportSource source, IReadOnlyList<UninheritedPoint> points, CancellationToken token)
    { TimingChoiceCalls++; return Choose?.Invoke(token) ?? Task.FromResult(Mode); }
    public Task<bool> ConfirmExclusionsAsync(OHsMidiImportSource source, CancellationToken token)
    { ExclusionCalls++; return Task.FromResult(AcceptExclusions); }
    public Task ShowErrorAsync(string title, string message, CancellationToken token) { Errors.Add((title, message)); return Task.CompletedTask; }
}
