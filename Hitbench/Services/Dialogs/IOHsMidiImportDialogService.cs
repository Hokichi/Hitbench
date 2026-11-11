using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services.Dialogs;

public interface IOHsMidiImportDialogService
{
    Task<string?> PickFileAsync(CancellationToken token);
    Task<OHsMidiImportMode?> ChooseTimingAsync(OHsMidiImportSource source, IReadOnlyList<UninheritedPoint> points, CancellationToken token);
    Task<bool> ConfirmExclusionsAsync(OHsMidiImportSource source, CancellationToken token);
    Task ShowErrorAsync(string title, string message, CancellationToken token);
}
