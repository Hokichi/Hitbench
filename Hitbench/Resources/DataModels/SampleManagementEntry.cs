using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace Hitbench.Resources.DataModels;

public sealed partial class SampleManagementEntry(string fileName) : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Extension))] private string fileName = fileName;
    [ObservableProperty] private Hitbench.Core.Entities.WaveformData? waveform;
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private bool isInteractionEnabled = true;
    public string Extension => Path.GetExtension(FileName);
}
