using CommunityToolkit.Mvvm.ComponentModel;
using Hitbench.Core.Entities;
using System.IO;
namespace Hitbench.Resources.DataModels;

public sealed partial class OHsDroppedSampleEntry(string sourcePath) : ObservableObject
{
    public string SourcePath { get; } = sourcePath;
    public string FileName => Path.GetFileName(SourcePath);
    [ObservableProperty] private bool isChecked = true;
    [ObservableProperty] private WaveformData? waveform;
    [ObservableProperty] private bool isPlaying;
}
