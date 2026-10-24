using Onset.Services.FileSystem;
namespace Onset.Resources.DataModels;
public sealed record OHsExportOptions(string DiffName, int StartingIndex, string SingleFormat, string MixedFormat, bool ShowFilesAfterExport, OHsExportDestination Destination = OHsExportDestination.Project, bool OverwriteEverything = false)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Destination) || DiffName is null || SampleFileNames.GetStemError(DiffName) is not null || StartingIndex is < 1 or > 999 ||
            SingleFormat is not (".ogg" or ".wav" or ".mp3") || MixedFormat is not (".ogg" or ".wav" or ".mp3"))
            throw new ArgumentException("Invalid export options.");
    }
}
