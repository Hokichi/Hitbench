using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace osuHitsounder.Resources.DataModels;

public sealed partial class SampleBrowserEntry : ObservableObject
{
    public SampleBrowserEntry(string fullPath, string name, bool isDirectory, bool isPlaceholder = false)
    {
        FullPath = fullPath;
        Name = name;
        IsDirectory = isDirectory;
        IsPlaceholder = isPlaceholder;
        if (isDirectory && !isPlaceholder) Children.Add(new("", "Loading...", false, true));
    }

    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public bool IsPlaceholder { get; }
    public string KindText => IsDirectory ? "Folder" : System.IO.Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    public ObservableCollection<SampleBrowserEntry> Children { get; } = [];
    [ObservableProperty] private bool isChecked;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isLoaded;
    [ObservableProperty] private string? error;
}
