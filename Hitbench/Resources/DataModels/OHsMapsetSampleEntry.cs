using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
namespace Hitbench.Resources.DataModels;
public sealed partial class OHsMapsetSampleEntry(string sourcePath) : ObservableObject
{
    public string SourcePath { get; } = sourcePath;
    public string FileName => Path.GetFileName(SourcePath);
    [ObservableProperty] private bool isChecked = true;
}
