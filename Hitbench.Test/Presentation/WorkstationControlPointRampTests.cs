using Hitbench.Core.Entities;
using Hitbench.Resources.Messages;
using Hitbench.Services;

namespace Hitbench.Test.Presentation;

public class WorkstationControlPointRampTests
{
    [Test]
    public async Task ModeEditKeepsVolumeAndRejectsStaleRequests()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 10 }, new() { Timestamp = 3000, Volume = 50 }] };
        var audio = new StubWorkstationAudioService();
        var owner = WorkstationControlPointStateTests.Create(project, audio);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner);
            var state = owner.ControlPointsState!;
            var request = new SetControlPointRequestedMessage(state.Context, state.Points[0].Id, null, null, state.Revision, IsRamp: true);
            owner.SetControlPointCommand.Execute(request);
            Assert.That((project.ControlPoints[0].Volume, project.ControlPoints[0].IsRamp), Is.EqualTo((10, true)));
            Assert.That(state.Points[0].IsRamp, Is.False);
            var snapshot = audio.ControlPoints;
            owner.SetControlPointCommand.Execute(request with { IsRamp = false });
            Assert.That(project.ControlPoints[0].IsRamp, Is.True);
            Assert.That(audio.ControlPoints, Is.SameAs(snapshot));
            state = owner.ControlPointsState!;
            owner.SetControlPointCommand.Execute(request with { ExpectedRevision = state.Revision });
            Assert.That(audio.ControlPoints, Is.SameAs(snapshot));
            owner.Position = TimeSpan.FromMilliseconds(1500);
            owner.SplitControlPointCommand.Execute(new SplitControlPointRequestedMessage(state.Context, state.Points[0].Id, state.Revision));
            var inserted = project.ControlPoints.Single(p => p.Timestamp == 1500);
            Assert.That((inserted.Volume, inserted.IsRamp), Is.EqualTo((30, true)));
        }
        finally { await owner.DeactivateAsync(); }
    }
}
