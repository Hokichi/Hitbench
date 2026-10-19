namespace osuHitsounder.Resources.DataModels;

public sealed record SamplePickerContext(string? ProjectPath, string? SongDirectory,
    SampleSelectionMode Mode = SampleSelectionMode.Multiple, string? InitialDirectory = null);
