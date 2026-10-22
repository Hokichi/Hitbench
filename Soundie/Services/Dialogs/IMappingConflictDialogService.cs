using Soundie.Resources.DataModels;

namespace Soundie.Services.Dialogs;

public interface IMappingConflictDialogService
{
    Task<IReadOnlyDictionary<string, string>?> SelectAsync(IReadOnlyList<MappingConflict> conflicts, CancellationToken token);
}
