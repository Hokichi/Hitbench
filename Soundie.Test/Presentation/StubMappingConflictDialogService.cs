using Soundie.Resources.DataModels;
using Soundie.Services.Dialogs;
using Soundie.ViewModels.Popups;

namespace Soundie.Test.Presentation;

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
