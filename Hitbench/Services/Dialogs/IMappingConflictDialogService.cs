using Hitbench.Resources.DataModels;

namespace Hitbench.Services.Dialogs;

public interface IMappingConflictDialogService
{
    Task<IReadOnlyDictionary<string, string>?> SelectAsync(IReadOnlyList<MappingConflict> conflicts, CancellationToken token);
}
