using Hitbench.Test;
using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Core.Entities;
using Hitbench.Resources.Helpers;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsControlPointRampMenuTests
{
    [Test]
    public void UnchangedLayoutRetainsPublishedCurveGeneration()
    {
        var point = new Hitbench.Resources.DataModels.ControlPointSnapshot(Guid.NewGuid(), 0, 50, "Normal");
        var lane = OHsControlPointLaneTests.Create([point]);
        var zone = lane.BringPointIntoView(point.Id)!;
        var curve = zone.Curve;
        UiTestPump.Layout(lane, 1000, 200);
        UiTestPump.Layout(lane, 1000, 200);
        Assert.That(zone.Curve, Is.SameAs(curve));
    }
    [Test]
    public void MenuConvertsBothWaysWithCapturedIdentityAndSharedStyle()
    {
        var project = new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), ControlPoints = [new() { Volume = 10 }, new() { Timestamp = 2000, Volume = 50 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner));
            var lane = OHsControlPointDragTests.Connect(owner);
            KeyboardNavigationPolicy.SetSuppressTabNavigation(lane, true);
            lane.PresentMenu = (menu, _) => UiTestPump.Layout(menu, 320, 240);
            var id = owner.ControlPointsState!.Points[0].Id;
            var zone = lane.BringPointIntoView(id)!;
            Assert.That(zone.IsTabStop, Is.False);
            lane.HandlePointKey(Key.Apps, ModifierKeys.None, zone);
            var mode = lane.ZoneMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Change to ramp"));
            UiControlContract.AssertStyle<MenuItem>(mode, "OHsActionMenuItemStyle");
            Assert.That(mode.IsTabStop, Is.False);
            mode.Command.Execute(mode.CommandParameter);
            Assert.That(project.ControlPoints[0].IsRamp, Is.True);
            lane.HandlePointKey(Key.Apps, ModifierKeys.None, lane.BringPointIntoView(id)!);
            mode = lane.ZoneMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Change to fixed"));
            mode.Command.Execute(mode.CommandParameter);
            Assert.That((project.ControlPoints[0].Volume, project.ControlPoints[0].IsRamp), Is.EqualTo((10, false)));
            lane.ResetInteraction();
            Assert.That(lane.MenuModeRequest, Is.Null);
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }
}
