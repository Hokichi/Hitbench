using Onset.Resources.DataModels;

namespace Onset.Services.Dialogs;

public interface IMappingConflictDialogService
{
    Task<IReadOnlyDictionary<string, string>?> SelectAsync(IReadOnlyList<MappingConflict> conflicts, CancellationToken token);
}
