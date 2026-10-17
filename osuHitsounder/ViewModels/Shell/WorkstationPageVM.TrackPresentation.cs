using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using osuHitsounder.Core.Constants;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    [RelayCommand(CanExecute = nameof(CanIncreaseTrackHeight))]
    private void IncreaseTrackHeight() => SetTrackHeight(trackHeight + 6);

    [RelayCommand(CanExecute = nameof(CanDecreaseTrackHeight))]
    private void DecreaseTrackHeight() => SetTrackHeight(trackHeight - 6);

    [RelayCommand(CanExecute = nameof(CanEditTracks))]
    private void ToggleTracksCollapsed()
    {
        if (!CanEditTracks) return;
        ApplyTrackCollapse(!AreTracksCollapsed);
        foreach (var track in tracks) track.IsCollapsed = AreTracksCollapsed;
        QueueSave();
    }

    private void ApplyTrackCollapse(bool collapsed)
    {
        if (!AreTracksCollapsed) expandedTrackGrowth = Math.Clamp(trackHeight - minimumTrackHeight, 0, 108);
        SetProperty(ref areTracksCollapsed, collapsed, nameof(AreTracksCollapsed));
        OnPropertyChanged(nameof(MaximumTrackHeight));
        SetProperty(ref trackHeight, minimumTrackHeight + (AreTracksCollapsed ? 0 : expandedTrackGrowth), nameof(TrackHeight));
        NotifyCommandState();
    }

    private bool CanIncreaseTrackHeight() => CanZoom && TrackHeight < MaximumTrackHeight;
    private bool CanDecreaseTrackHeight() => CanZoom && TrackHeight > MinimumTrackHeight;
    private void SetTrackHeight(double value)
    {
        if (!CanZoom) return;
        if (SetProperty(ref trackHeight, Math.Clamp(value, MinimumTrackHeight, MaximumTrackHeight), nameof(TrackHeight)))
            NotifyCommandState();
    }

    public void SetTrackHeightMinimum(double minimum)
    {
        if (deactivated || !double.IsFinite(minimum) || minimum <= 0 || minimum == minimumTrackHeight) return;
        var growth = AreTracksCollapsed ? 0 : Math.Clamp(trackHeight - minimumTrackHeight, 0, 108);
        minimumTrackHeight = minimum;
        OnPropertyChanged(nameof(MinimumTrackHeight));
        OnPropertyChanged(nameof(MaximumTrackHeight));
        SetProperty(ref trackHeight, minimum + growth, nameof(TrackHeight));
        NotifyCommandState();
    }

    private double trackHeight = 36;
    private double minimumTrackHeight = 36;
    private bool areTracksCollapsed;
    private double expandedTrackGrowth;
    public bool AreTracksCollapsed => areTracksCollapsed;
    public double MinimumTrackHeight => minimumTrackHeight;
    public double MaximumTrackHeight => minimumTrackHeight + (AreTracksCollapsed ? 0 : 108);
    public double TrackHeight => trackHeight;
    private void InitializeTrackPresentation(ProjectData project) => ApplyTrackCollapse(project.Tracks.Count > 0 && project.Tracks.All(t => t.IsCollapsed));
}
