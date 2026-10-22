using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Soundie.Resources.DataModels;

public sealed partial class MappingConflictChoice(string key, string filePath, bool isExisting, bool isMissing) : ObservableObject
{
    public string FilePath { get; } = filePath;
    public string FileName => Path.GetFileName(FilePath);
    public bool IsExisting { get; } = isExisting;
    public bool IsMissing { get; } = isMissing;
    public string AvailabilityText => IsMissing ? "Missing file" : "";
    public string AutomationName => string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} · {1} · {2}", key, IsExisting ? "Existing" : "Incoming", FilePath);
    [ObservableProperty] private bool isChecked;
}
