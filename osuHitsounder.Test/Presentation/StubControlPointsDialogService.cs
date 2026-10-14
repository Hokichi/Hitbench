using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.Dialogs;
namespace osuHitsounder.Test.Presentation;

public sealed class StubControlPointsDialogService : IControlPointsDialogService
{
    public Func<ControlPointsSnapshot, CancellationToken, Task>? Show { get; set; }
    public Task ShowAsync(ControlPointsSnapshot initial, CancellationToken token) => Show?.Invoke(initial, token) ?? Task.CompletedTask;
}
