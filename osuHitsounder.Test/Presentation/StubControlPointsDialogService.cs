using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.Dialogs;
namespace osuHitsounder.Test.Presentation;

public sealed class StubControlPointsDialogService : IControlPointsDialogService
{
    public Func<ControlPointsSnapshot, CancellationToken, Task>? Show { get; set; }
    public Func<ControlPointSnapshot, int, int, CancellationToken, Task<bool>>? Confirm { get; set; }
    public Task<bool> ConfirmReplaceAsync(ControlPointSnapshot selected, int targetTimestamp, int conflictingPointCount, CancellationToken token) =>
        Confirm?.Invoke(selected, targetTimestamp, conflictingPointCount, token) ?? Task.FromResult(false);
    public Task ShowAsync(ControlPointsSnapshot initial, CancellationToken token) => Show?.Invoke(initial, token) ?? Task.CompletedTask;
}
