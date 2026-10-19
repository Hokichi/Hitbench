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
    [ObservableProperty][NotifyPropertyChangedFor(nameof(DurationText))][NotifyPropertyChangedFor(nameof(SizeText))][NotifyPropertyChangedFor(nameof(SelectionSubtitle))] private SampleFileMetadata? metadata;
    [ObservableProperty] private bool isMetadataLoading;
    [ObservableProperty] private bool isMetadataUnavailable;
    [ObservableProperty] private bool isAlreadyInProject;
    [ObservableProperty] private bool isPreviewing;
    public string SelectionSubtitle => $"{SourceFolderName} · {DurationText}";
    public string SourceFolderName => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(FullPath)) ?? FullPath;
    public string DurationText => IsDirectory ? "" : Metadata is { } data ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:0.000} s", data.Duration.TotalSeconds) : "—";
    public string SizeText => IsDirectory ? "" : Metadata is { } data ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:N0} KiB", data.Length / 1024d) : "—";
}
