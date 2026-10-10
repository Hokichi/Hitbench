using CommunityToolkit.Mvvm.ComponentModel;
namespace osuHitsounder.Resources.DataModels;
public sealed partial class SampleBrowserEntry(string fullPath, string name, bool isDirectory) : ObservableObject
{
    public string FullPath { get; } = fullPath;
    public string Name { get; } = name;
    public bool IsDirectory { get; } = isDirectory;
    public string KindText => IsDirectory ? "Folder" : System.IO.Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    [ObservableProperty] private bool isChecked;
    [ObservableProperty] private bool isSelected;
}
