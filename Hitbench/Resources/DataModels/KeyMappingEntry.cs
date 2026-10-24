using CommunityToolkit.Mvvm.ComponentModel;
namespace Hitbench.Resources.DataModels;

public sealed partial class KeyMappingEntry(string key) : ObservableObject
{
    public string Key { get; } = key;
    [ObservableProperty] private string draftFileName = "";
    [ObservableProperty] private string? fileName;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasError))] private string? error;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(AvailabilityText))][NotifyPropertyChangedFor(nameof(MappingStatus))] private bool isMissing;
    public bool IsMapped => FileName is not null;
    public string KeyboardLabel => Key.StartsWith("C", StringComparison.Ordinal) && !IsSharp ? Key : "";
    public bool IsSharp => Key.Contains('#');
    public string MappingStatus => FileName is null ? "Unmapped" : IsMissing ? "Missing file" : "Mapped";
    public bool HasError => Error is not null;
    public string AvailabilityText => FileName is null ? "Unmapped" : IsMissing ? "Missing file" : "";
    partial void OnFileNameChanged(string? value) { OnPropertyChanged(nameof(AvailabilityText)); OnPropertyChanged(nameof(IsMapped)); OnPropertyChanged(nameof(MappingStatus)); }
}
