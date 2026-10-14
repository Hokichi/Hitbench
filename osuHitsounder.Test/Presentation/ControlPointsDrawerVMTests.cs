using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public class ControlPointsDrawerVMTests
{
    [Test]
    public async Task TimestampActionsAreScopedAndMovedSnapshotsPreserveDraftsAndIdentity()
    {
        var context = new ControlPointsContext("project.json", Guid.NewGuid(), Guid.NewGuid());
        var first = new ControlPointSnapshot(Guid.NewGuid(), 500, 50, "Drum");
        var second = new ControlPointSnapshot(Guid.NewGuid(), 1000, 75, "Normal");
        var messages = new WeakReferenceMessenger();
        var deletes = new List<DeleteControlPointRequestedMessage>(); var snaps = new List<SnapControlPointRequestedMessage>();
        messages.Register<DeleteControlPointRequestedMessage>(this, (_, request) => deletes.Add(request));
        messages.Register<SnapControlPointRequestedMessage>(this, (_, request) => snaps.Add(request));
        var vm = new ControlPointsDrawerVM(new(context, [first, second], 1, true, null, false, null), messages, new ImmediateUiScheduler()); vm.Initialize();
        try
        {
            var row = vm.Rows[0]; var timestampNotifications = 0;
            row.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(row.Timestamp)) timestampNotifications++; };
            vm.DeletePointCommand.Execute(first.Id); vm.SnapPointCommand.Execute(first.Id);
            Assert.That(deletes.Single(), Is.EqualTo(new DeleteControlPointRequestedMessage(context, first.Id)));
            Assert.That(snaps.Single(), Is.EqualTo(new SnapControlPointRequestedMessage(context, first.Id)));
            vm.DeletePointCommand.Execute(Guid.NewGuid()); vm.SnapPointCommand.Execute(Guid.Empty);
            row.Volume = 4; Assert.That(row.Error, Does.Contain("5 to 100")); Assert.That(vm.PrepareClose(), Is.False);
            messages.Send(new ControlPointsChangedMessage(new(context, [first with { Timestamp = 1500 }, second], 2, false, "No timing.", false, null)));
            Assert.That(vm.Rows[1], Is.SameAs(row)); Assert.That(row.Timestamp, Is.EqualTo(1500));
            Assert.That(row.Volume, Is.EqualTo(4)); Assert.That(row.IsDirty, Is.True); Assert.That(timestampNotifications, Is.EqualTo(1));
            Assert.That(vm.CanSnap, Is.False); vm.SnapPointCommand.Execute(first.Id); Assert.That(snaps, Has.Count.EqualTo(1));
            row.Volume = 5; Assert.That(row.Error, Is.Null); row.Volume = 100; Assert.That(row.Error, Is.Null);
            messages.Send(new ControlPointsChangedMessage(new(context, [second], 3, true, null, false, null)));
            Assert.That(vm.DeletePointCommand.CanExecute(first.Id), Is.False); vm.DeletePointCommand.Execute(first.Id);
            await vm.DeactivateAsync(); Assert.That(vm.SnapPointCommand.CanExecute(second.Id), Is.False);
            vm.DeletePointCommand.Execute(second.Id); vm.SnapPointCommand.Execute(second.Id);
            Assert.That(deletes, Has.Count.EqualTo(1)); Assert.That(snaps, Has.Count.EqualTo(1));
        }
        finally { await vm.DeactivateAsync(); messages.UnregisterAll(this); }
    }
    [Test]
    public async Task DraftsCommandsSnapshotsAndDeactivationAreSessionScoped()
    {
        var ctx=new ControlPointsContext("project.json",Guid.NewGuid(),Guid.NewGuid()); var point=new ControlPointSnapshot(Guid.NewGuid(),1000,50,"Normal");
        var messenger=new WeakReferenceMessenger(); var requests=new List<SetControlPointRequestedMessage>(); var adds=0; var retries=0;
        messenger.Register<SetControlPointRequestedMessage>(this,(_,m)=>requests.Add(m));
        messenger.Register<AddControlPointRequestedMessage>(this,(_,_)=>adds++);
        messenger.Register<RetryControlPointsSaveRequestedMessage>(this,(_,_)=>retries++);
        var vm=new ControlPointsDrawerVM(new(ctx,[point],1,true,null,false,null),messenger,new ImmediateUiScheduler()); vm.Initialize();
        try
        {
            vm.AddCommand.Execute(null); Assert.That(adds,Is.EqualTo(1));
            var row=vm.Rows.Single(); row.Volume=50.5m; Assert.That(requests,Is.Empty); Assert.That(row.Error,Is.Not.Null);
            messenger.Send(new ControlPointsChangedMessage(new(ctx,[point],2,true,null,true,null)));
            Assert.That(row.Volume,Is.EqualTo(50.5m));
            row.Volume=25; Assert.That(requests.Single().Volume,Is.EqualTo(25));
            row.Sampleset="Drum"; Assert.That(requests.Last().Sampleset,Is.EqualTo("Drum"));
            messenger.Send(new ControlPointsChangedMessage(new(ctx,[point with {Volume=25,Sampleset="Drum"}],3,true,null,false,"Disk full")));
            Assert.That(vm.SaveError,Is.EqualTo("Disk full")); vm.RetrySaveCommand.Execute(null); Assert.That(retries,Is.EqualTo(1));
            messenger.Send(new ControlPointsChangedMessage(new(ctx,[point],1,false,null,false,null))); Assert.That(vm.SaveError,Is.EqualTo("Disk full"));
            await vm.DeactivateAsync(); Assert.That(vm.AddCommand.CanExecute(null),Is.False);
            row.Volume=75; Assert.That(requests,Has.Count.EqualTo(2));
        }
        finally { await vm.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
}
