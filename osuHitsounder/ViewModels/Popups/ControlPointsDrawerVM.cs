using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.ViewModels.Popups;

public sealed partial class ControlPointsDrawerVM : ObservableObject
{
    private ControlPointsSnapshot snapshot;
    private readonly IMessenger messenger;
    private readonly IUiScheduler scheduler;
    private readonly ObservableCollection<ControlPointEntry> rows = [];
    private Task notifications = Task.CompletedTask;
    private bool closed, registered, applying;
    public ReadOnlyObservableCollection<ControlPointEntry> Rows { get; }
    public bool CanAdd => !closed && snapshot.CanAdd;
    public bool CanSnap => !closed && snapshot.CanAdd;
    private bool CanDeletePoint(Guid pointId) => !closed && rows.Any(row => row.Id == pointId);
    private bool CanSnapPoint(Guid pointId) => CanSnap && CanDeletePoint(pointId);
    [RelayCommand(CanExecute = nameof(CanDeletePoint))]
    private void DeletePoint(Guid pointId)
    {
        if (CanDeletePoint(pointId)) messenger.Send(new DeleteControlPointRequestedMessage(snapshot.Context, pointId));
    }
    [RelayCommand(CanExecute = nameof(CanSnapPoint))]
    private void SnapPoint(Guid pointId)
    {
        if (CanSnapPoint(pointId)) messenger.Send(new SnapControlPointRequestedMessage(snapshot.Context, pointId));
    }
    public string? UnavailableReason => snapshot.UnavailableReason;
    public string? SaveError => snapshot.SaveError;
    public string SaveStatus => SaveError is not null ? "Save failed" : snapshot.IsSaving ? "Saving…" : "Autosaved";
    public bool IsEmpty => Rows.Count == 0;
    public bool CanClose => !closed;
    public bool CanRetry => !closed && SaveError is not null;
    public ControlPointsDrawerVM(ControlPointsSnapshot initial, IMessenger messenger, IUiScheduler scheduler)
    {
        snapshot = initial; this.messenger = messenger; this.scheduler = scheduler;
        Rows = new(rows); ApplySnapshot(initial);
    }
    public void Initialize()
    {
        if (closed || registered) return; registered = true;
        messenger.Register<ControlPointsChangedMessage>(this, (_, message) => notifications = ApplyNotificationAsync(notifications, message.Snapshot));
        messenger.Send(new ControlPointsReadyMessage(snapshot.Context));
    }
    private async Task ApplyNotificationAsync(Task previous, ControlPointsSnapshot next)
    {
        await previous;
        try { await scheduler.InvokeAsync(() => { if (!closed && next.Context == snapshot.Context && next.Version >= snapshot.Version) ApplySnapshot(next); }, default); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Control point notification failed: {0}", error); }
    }
    private void ApplySnapshot(ControlPointsSnapshot next)
    {
        applying = true;
        try
        {
            snapshot = next;
            var ids = next.Points.Select(p => p.Id).ToHashSet();
            foreach (var row in rows.Where(r => !ids.Contains(r.Id)).ToArray()) { row.PropertyChanged -= OnEntryChanged; rows.Remove(row); }
            var index = 0;
            foreach (var point in next.Points.OrderBy(p => p.Timestamp))
            {
                var row = rows.FirstOrDefault(r => r.Id == point.Id);
                if (row is null) { row = new(point); row.PropertyChanged += OnEntryChanged; rows.Insert(index, row); }
                else { row.ApplySnapshot(point); if (rows.IndexOf(row) != index) rows.Move(rows.IndexOf(row), index); }
                index++;
            }
        }
        finally { applying = false; }
        NotifyState();
    }
    private void OnEntryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (closed || applying || sender is not ControlPointEntry row || args.PropertyName is not (nameof(ControlPointEntry.Volume) or nameof(ControlPointEntry.Sampleset))) return;
        var error = row.Volume is < 5 or > 100 || decimal.Truncate(row.Volume) != row.Volume ? "Enter a whole number from 5 to 100."
            : !Track.SamplesetOptions.Contains(row.Sampleset) ? "Choose Normal, Soft, or Drum." : null;
        row.SetError(error);
        if (error is null && row.IsDirty) messenger.Send(new SetControlPointRequestedMessage(snapshot.Context, row.Id, row.Volume, row.Sampleset));
    }
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add() { if (CanAdd) messenger.Send(new AddControlPointRequestedMessage(snapshot.Context)); }
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void RetrySave() { if (CanRetry) messenger.Send(new RetryControlPointsSaveRequestedMessage(snapshot.Context)); }
    public bool PrepareClose() => CanClose && rows.All(r => r.Error is null);
    public async Task DeactivateAsync()
    {
        closed = true; messenger.UnregisterAll(this);
        foreach (var row in rows) row.PropertyChanged -= OnEntryChanged;
        NotifyState(); await notifications;
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(CanAdd), nameof(CanSnap), nameof(UnavailableReason), nameof(SaveError), nameof(SaveStatus), nameof(IsEmpty), nameof(CanClose), nameof(CanRetry) }) OnPropertyChanged(name);
        AddCommand.NotifyCanExecuteChanged(); RetrySaveCommand.NotifyCanExecuteChanged();
        DeletePointCommand.NotifyCanExecuteChanged(); SnapPointCommand.NotifyCanExecuteChanged();
    }
}
