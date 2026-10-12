using CommunityToolkit.Mvvm.ComponentModel;
namespace osuHitsounder.Resources.DataModels;
public sealed partial class KeyMappingEntry(string key) : ObservableObject
{
 public string Key {get;}=key;
 [ObservableProperty] private string draftFileName="";
 [ObservableProperty] private string? fileName;
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string? error;
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(AvailabilityText))] private bool isMissing;
 public bool HasError => Error is not null;
 public string AvailabilityText => FileName is null ? "Unmapped" : IsMissing ? "Missing file" : "";
 partial void OnFileNameChanged(string? value) => OnPropertyChanged(nameof(AvailabilityText));
}
