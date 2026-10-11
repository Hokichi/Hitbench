using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace osuHitsounder.Resources.DataModels;

public sealed partial class SampleManagementEntry(string fileName) : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Extension))] private string fileName = fileName;
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private bool isPlaying;
    public string Extension => Path.GetExtension(FileName);
}
