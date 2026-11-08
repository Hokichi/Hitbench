using System.Collections.ObjectModel;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;
namespace Hitbench.ViewModels.Popups;
public sealed partial class ManageSamplesPopupVM
{
    private IReadOnlyList<string> allSampleFiles = [];
    public ObservableCollection<SampleManagementEntry> MapsetEntries { get; } = [];
    private IEnumerable<SampleManagementEntry> ManagedEntries => Entries.Concat(MapsetEntries);
    private bool IsManagedRow(SampleManagementEntry row) => Entries.Contains(row) || MapsetEntries.Contains(row);
    private bool IsManagedFile(string name) => !name.Contains('/') && !name.Contains('\\') ||
        !Context.HasMapsetFolderConflict && OHsMapsetSamples.IsMapsetSamplePath(name);
}
