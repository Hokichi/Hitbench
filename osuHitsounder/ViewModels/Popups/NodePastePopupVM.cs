using CommunityToolkit.Mvvm.ComponentModel;
using osuHitsounder.Services;

namespace osuHitsounder.ViewModels.Popups;

public partial class NodePastePopupVM : ObservableObject
{
    [ObservableProperty] private NodePasteTiming timing = NodePasteTiming.PlaybackMarker;
    public bool UseSourceTimestamps { get => Timing == NodePasteTiming.SourceTimestamps; set { if (value) Timing = NodePasteTiming.SourceTimestamps; } }
    public bool UsePlaybackMarker { get => Timing == NodePasteTiming.PlaybackMarker; set { if (value) Timing = NodePasteTiming.PlaybackMarker; } }
    partial void OnTimingChanged(NodePasteTiming value) { OnPropertyChanged(nameof(UseSourceTimestamps)); OnPropertyChanged(nameof(UsePlaybackMarker)); }
}
