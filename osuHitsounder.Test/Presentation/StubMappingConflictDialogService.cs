using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public sealed class StubMappingConflictDialogService : IMappingConflictDialogService
{
    public IReadOnlyList<MappingConflict>? Conflicts { get; private set; }
    public Func<IReadOnlyList<MappingConflict>, CancellationToken, Task<IReadOnlyDictionary<string, string>?>>? Select { get; set; }
    public Task<IReadOnlyDictionary<string, string>?> SelectAsync(IReadOnlyList<MappingConflict> conflicts, CancellationToken token)
    {
        Conflicts = conflicts;
        return Select?.Invoke(conflicts, token) ?? Task.FromResult<IReadOnlyDictionary<string, string>?>(new MappingConflictsPopupVM(conflicts).Confirm());
    }
}
