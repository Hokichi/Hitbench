using System.Collections.ObjectModel;
using osuHitsounder.Core.Constants;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.ViewModels.Popups;

public sealed class MappingConflictsPopupVM(IReadOnlyList<MappingConflict> conflicts)
{
    public IReadOnlyList<MappingConflictEntry> Rows { get; } = Array.AsReadOnly(conflicts.OrderBy(c =>
        InstrumentalKeys.TryGetIndex(c.Key, out var index) ? index : int.MaxValue).Select(c => new MappingConflictEntry(c)).ToArray());
    public bool CanConfirm => Rows.Count > 0 && Rows.All(row => row.Choices.Count(choice => choice.IsChecked) == 1);
    public IReadOnlyDictionary<string, string> Confirm() => CanConfirm
        ? new ReadOnlyDictionary<string, string>(Rows.ToDictionary(row => row.Key, row => row.Choices.Single(choice => choice.IsChecked).FilePath))
        : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
