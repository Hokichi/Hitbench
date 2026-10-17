using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
namespace osuHitsounder.ViewModels.Shell;
public partial class WorkstationPageVM
{
    private readonly Dictionary<Track, (double Collapsed, double Expanded)> trackExtents = new(ReferenceEqualityComparer.Instance);
    private double trackHeight = 36, minimumTrackHeight = 36, expandedTrackGrowth;
    private int trackPresentationRevision;
    public int TrackPresentationRevision => trackPresentationRevision;
    public bool AreTracksCollapsed => tracks.Count > 0 && tracks.All(t => t.IsCollapsed);
    public bool HasCollapsedTracks => tracks.Any(t => t.IsCollapsed);
    public bool CanToggleTracksCollapsed => CanEditTracks && tracks.Count > 0;
    public string CollapseTracksActionText => AreTracksCollapsed ? "Expand all tracks" : "Collapse all tracks";
    public double MinimumTrackHeight => minimumTrackHeight;
    public double MaximumTrackHeight => minimumTrackHeight + (AreTracksCollapsed ? 0 : 108);
    public double TrackHeight => AreTracksCollapsed ? minimumTrackHeight : trackHeight;
    public double GetTrackExtent(Track track) => track.IsCollapsed ? trackExtents.GetValueOrDefault(track, (36,36)).Collapsed : trackHeight;
    public void SetTrackMinimumExtent(Track track, bool collapsed, double minimum)
    {
        if (deactivated || !tracks.Contains(track) || !double.IsFinite(minimum) || minimum <= 0) return;
        var old = trackExtents.GetValueOrDefault(track, (36,36));
        var next = collapsed ? (minimum, old.Expanded) : (old.Collapsed, minimum);
        if (old == next) return;
        trackExtents[track] = next;
        if (!collapsed) SetTrackHeightMinimum(trackExtents.Values.Max(v => v.Expanded));
        NotifyTrackPresentation();
    }
    public void SetTrackHeightMinimum(double minimum)
    {
        if (deactivated || !double.IsFinite(minimum) || minimum <= 0 || minimum == minimumTrackHeight) return;
        minimumTrackHeight = minimum; trackHeight = minimum + expandedTrackGrowth;
        NotifyTrackPresentation();
    }
    [RelayCommand(CanExecute = nameof(CanToggleTracksCollapsed))]
    private void ToggleTracksCollapsed()
    {
        if (!CanToggleTracksCollapsed) return;
        var value = !AreTracksCollapsed;
        OnPropertyChanging(nameof(AreTracksCollapsed));
        foreach (var track in tracks) track.IsCollapsed = value;
        NotifyTrackPresentation(); QueueSave();
    }
    [RelayCommand(CanExecute = nameof(CanCollapseTrack))]
    private void ToggleTrackCollapsed(Track track) => SetTrackCollapsed(track, !track.IsCollapsed);
    private bool CanCollapseTrack(Track? track) => CanEditTracks && track is not null && tracks.Contains(track);
    public void SetTrackCollapsed(Track track, bool collapsed)
    {
        if (!CanCollapseTrack(track) || track.IsCollapsed == collapsed) return;
        track.IsCollapsed = collapsed; NotifyTrackPresentation(); QueueSave();
    }
    [RelayCommand(CanExecute = nameof(CanIncreaseTrackHeight))]
    private void IncreaseTrackHeight() => SetTrackHeight(trackHeight + 6);
    [RelayCommand(CanExecute = nameof(CanDecreaseTrackHeight))]
    private void DecreaseTrackHeight() => SetTrackHeight(trackHeight - 6);
    private bool CanIncreaseTrackHeight() => CanZoom && !AreTracksCollapsed && trackHeight < MaximumTrackHeight;
    private bool CanDecreaseTrackHeight() => CanZoom && !AreTracksCollapsed && trackHeight > MinimumTrackHeight;
    private void SetTrackHeight(double value)
    {
        if (!CanZoom || AreTracksCollapsed) return;
        expandedTrackGrowth = Math.Clamp(value - minimumTrackHeight, 0,108);
        trackHeight = minimumTrackHeight + expandedTrackGrowth; NotifyTrackPresentation();
    }
    private void NotifyTrackPresentation()
    {
        foreach (var name in new[] { nameof(AreTracksCollapsed), nameof(HasCollapsedTracks), nameof(CanToggleTracksCollapsed), nameof(CollapseTracksActionText), nameof(TrackHeight), nameof(MinimumTrackHeight), nameof(MaximumTrackHeight) }) OnPropertyChanged(name);
        trackPresentationRevision++; OnPropertyChanged(nameof(TrackPresentationRevision)); NotifyTrackCommands();
    }
    private void InitializeTrackPresentation(ProjectData project)
    {
        if (RenamingTrack is {} editing) CancelTrackRename(editing);
        trackExtents.Clear(); minimumTrackHeight = trackHeight = 36; expandedTrackGrowth = 0;
    }
}
