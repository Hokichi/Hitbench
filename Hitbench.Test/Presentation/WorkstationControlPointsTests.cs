using Hitbench.Core.Entities;
using Hitbench.Resources.Messages;

namespace Hitbench.Test.Presentation;

public class WorkstationControlPointsTests
{
    [Test]
    public async Task InvalidSamplesetAndNoOpLeaveAcceptedIdentityAndTimelineUnchanged()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Volume = 4 }] };
        var audio = new StubWorkstationAudioService(); var files = new DelayedProjectFileService();
        var owner = WorkstationControlPointStateTests.Create(project, audio, files);
        try
        {
            await WorkstationControlPointStateTests.Ready(owner); var state = owner.ControlPointsState!; var timeline = audio.ControlPoints;
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, state.Points[0].Id, 50, "Bad"));
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, state.Points[0].Id, null, "Normal"));
            owner.SetControlPointCommand.Execute(new SetControlPointRequestedMessage(state.Context, Guid.NewGuid(), 50, null));
            Assert.That(project.ControlPoints.Single().Volume, Is.EqualTo(4));
            Assert.That(owner.ControlPointsState!.Revision, Is.EqualTo(state.Revision));
            Assert.That(audio.ControlPoints, Is.SameAs(timeline)); Assert.That(files.Writes, Is.Zero);
        }
        finally { files.Release(); await owner.DeactivateAsync(); }
    }
}
