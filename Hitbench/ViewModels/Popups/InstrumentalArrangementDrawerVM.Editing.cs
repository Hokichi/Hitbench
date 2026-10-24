using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Constants;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services;
namespace Hitbench.ViewModels.Popups;

public sealed partial class InstrumentalArrangementDrawerVM
{
    private IReadOnlyList<Guid> selectedNoteIds = [];
    private InstrumentalNoteSnapshot[] clipboard = [];
    public IReadOnlyList<Guid> SelectedNoteIds => selectedNoteIds;
    public bool IsAdditionMode => timeline.IsAdditionMode;
    public void RequestEditingMode(bool value) { if (CanEdit) messenger.Send(new OHsInstrumentalTimelineEditRequestedMessage(snapshot.Context, ++requestSequence, IsAdditionMode: value)); }
    public bool IsSelectionMode => !IsAdditionMode;
    private double laneHeight = 36;
    public double LaneHeight { get => laneHeight; set { if (double.IsFinite(value)) SetProperty(ref laneHeight, Math.Clamp(value,36,144)); } }
    public bool CanUndo => CanEdit && snapshot.CanUndo;
    public bool CanRedo => CanEdit && snapshot.CanRedo;
    public bool CanCopyNotes => CanEdit && SelectedNoteIds.Count > 0;
    public bool CanPasteNotes => CanEdit && clipboard.Length > 0;
    public bool CanDeleteNotes => CanCopyNotes;
    public void SetNoteSelection(IReadOnlyList<Guid> ids, Guid? anchorId)
    {
        var liveIds = Notes.Select(n => n.Id).ToHashSet();
        selectedNoteIds = Array.AsReadOnly(ids.Distinct().Where(liveIds.Contains).ToArray());
        SelectedNoteId = anchorId is { } anchor && selectedNoteIds.Contains(anchor) ? anchor : selectedNoteIds.Count > 0 ? selectedNoteIds[0] : null;
        if (selectedNoteIds.Count > 0) RequestFollow(false);
        OnPropertyChanged(nameof(SelectedNoteIds)); NotifyNoteCommands();
    }
    private void NotifyNoteCommands()
    {
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(CanCopyNotes)); OnPropertyChanged(nameof(CanPasteNotes)); OnPropertyChanged(nameof(CanDeleteNotes));
        UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged(); CopyNotesCommand.NotifyCanExecuteChanged(); PasteNotesCommand.NotifyCanExecuteChanged(); DeleteSelectedNotesCommand.NotifyCanExecuteChanged(); SelectAllNotesCommand.NotifyCanExecuteChanged();
    }
    private void ReplaceNotes(IReadOnlyList<InstrumentalNoteSnapshot> desired, IReadOnlyList<Guid> affected)
    { if (!CanEdit) return; pendingRequest=Guid.NewGuid(); messenger.Send(new ReplaceInstrumentalNotesRequestedMessage(snapshot.Context,pendingRequest,snapshot.Revision,desired,affected)); }
    public void MoveNotes(IReadOnlyList<Guid> ids, Guid anchorId, string targetKey, double requestedMilliseconds)
    {
        var anchor=Notes.FirstOrDefault(n=>n.Id==anchorId);
        if (!CanEdit || anchor is null || !ids.Contains(anchorId) || !InstrumentalKeys.TryGetIndex(targetKey,out var targetPitch) || !InstrumentalKeys.TryGetIndex(anchor.Key,out var originalPitch) || !double.IsFinite(requestedMilliseconds) || requestedMilliseconds < 0 || requestedMilliseconds >= Duration.TotalMilliseconds) return;
        var time=BeatGridCalculator.GetNearestTimestamp(Offsets,Bpms,Subdivision,requestedMilliseconds,Duration.TotalMilliseconds);
        if(time is null)return;
        var delta=(long)time.Value-anchor.Timestamp; var pitches=targetPitch-originalPitch;
        var movingIds = ids.ToHashSet();
        var result=new List<InstrumentalNoteSnapshot>();
        foreach(var note in Notes)
        {
            if(!movingIds.Contains(note.Id)){result.Add(note);continue;}
            InstrumentalKeys.TryGetIndex(note.Key,out var pitch); var timestamp=note.Timestamp+delta;
            if(pitch+pitches is <0 or >107 || timestamp<0 || timestamp>=Duration.TotalMilliseconds || timestamp>int.MaxValue){ErrorMessage="The selected notes do not fit within the piano roll.";return;}
            result.Add(note with{Key=InstrumentalKeys.GetName(pitch+pitches),Timestamp=(int)timestamp});
        }
        ReplaceNotes(result,ids);
    }
    public void RemoveNotes(IReadOnlyList<Guid> ids)
    { var removed = ids.ToHashSet(); ReplaceNotes(Notes.Where(n=>!removed.Contains(n.Id)).ToArray(),ids); }
    [RelayCommand(CanExecute=nameof(CanEdit))] private void SelectAllNotes()=>SetNoteSelection(Notes.Select(n=>n.Id).ToArray(),Notes.FirstOrDefault()?.Id);
    [RelayCommand(CanExecute=nameof(CanCopyNotes))] private void CopyNotes()
    { var selected = SelectedNoteIds.ToHashSet(); var notes=Notes.Where(n=>selected.Contains(n.Id)).ToArray(); if(notes.Length==0)return; var first=notes.Min(n=>n.Timestamp); clipboard=notes.Select(n=>n with{Timestamp=n.Timestamp-first}).ToArray();NotifyNoteCommands(); }
    [RelayCommand(CanExecute=nameof(CanPasteNotes))] private void PasteNotes()
    {
        var anchor=BeatGridCalculator.GetNearestTimestamp(Offsets,Bpms,Subdivision,Position.TotalMilliseconds,Duration.TotalMilliseconds);if(anchor is null)return;
        var added=clipboard.Where(n=>(long)anchor.Value+n.Timestamp<Duration.TotalMilliseconds && (long)anchor.Value+n.Timestamp<=int.MaxValue).Select(n=>n with{Id=Guid.NewGuid(),Timestamp=anchor.Value+n.Timestamp}).ToArray();
        if(added.Length>0)ReplaceNotes([..Notes,..added],added.Select(n=>n.Id).ToArray());
    }
    [RelayCommand(CanExecute=nameof(CanDeleteNotes))] private void DeleteSelectedNotes()=>RemoveNotes(SelectedNoteIds);
    [RelayCommand(CanExecute=nameof(CanUndo))] private void Undo()=>ReplayHistory(false);
    [RelayCommand(CanExecute=nameof(CanRedo))] private void Redo()=>ReplayHistory(true);
    private void ReplayHistory(bool redo){pendingRequest=Guid.NewGuid();messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(snapshot.Context,pendingRequest,redo));}
}
