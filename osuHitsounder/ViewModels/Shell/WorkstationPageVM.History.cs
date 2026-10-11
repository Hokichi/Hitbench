using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Services;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private readonly NodeHistoryManager history = new();

    public bool CanUndo => CanPlaceSamples && history.CanUndo;
    public bool CanRedo => CanPlaceSamples && history.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (CanUndo && history.Undo(tracks)) PublishNodeChange();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (CanRedo && history.Redo(tracks)) PublishNodeChange();
    }

    private void PublishNodeChange()
    {
        CancelNodeDrag();
        PruneNodeSelection();
        samplesRevision++;
        OnPropertyChanged(nameof(SamplesRevision));
        NotifyHistoryState();
        QueuePlaybackUpdate();
        QueueSave();
    }

    private void NotifyHistoryState()
    {
        NotifyTrackAuditionState();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        ResnapAllSamplesCommand.NotifyCanExecuteChanged();
    }
}
