using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public class ControlPointsDrawerVMTests
{
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
