using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Soundie.Core.Entities;
using Soundie.Resources.DataModels;
using Soundie.Views.Pages;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsControlPointZoneTests
{
    [Test]
    public void ZoneMenuUsesCapturedIdentityAndLiveSplitEligibility()
    {
        var project = new ProjectData { Offsets = [0], BPMs = [120], ControlPoints = [new() { Timestamp = 1000 }, new() { Timestamp = 3000 }] };
        var owner = WorkstationControlPointStateTests.Create(project, new());
        try
        {
            UiTestPump.Run(WorkstationControlPointStateTests.Ready(owner)); var lane = OHsControlPointDragTests.Connect(owner);
            lane.SetPointDisabledCommand = owner.SetControlPointDisabledCommand; lane.SplitPointCommand = owner.SplitControlPointCommand;
            lane.PresentMenu = (menu, _) => UiTestPump.Layout(menu, 240, 180); owner.Position = TimeSpan.FromMilliseconds(2000);
            var state = owner.ControlPointsState!; var zone = lane.BringPointIntoView(state.Points[0].Id)!;
            Assert.That(lane.HandlePointKey(Key.Apps, ModifierKeys.None, zone), Is.True);
            var disable = (MenuItem)lane.ZoneMenu.Items[0]; var split = (MenuItem)lane.ZoneMenu.Items[1];
            Assert.That(disable.Header, Is.EqualTo("Disable")); Assert.That(split.Header, Is.EqualTo("Split"));
            Assert.That(split.Command, Is.SameAs(owner.SplitControlPointCommand)); Assert.That(split.IsEnabled, Is.True);
            owner.Position = TimeSpan.FromMilliseconds(3500); Assert.That(split.IsEnabled, Is.False);
            owner.Position = TimeSpan.FromMilliseconds(2000); Assert.That(split.IsEnabled, Is.True);
            var captured = lane.MenuSplitRequest!;
            zone.Point = state.Points[1];
            disable.Command.Execute(disable.CommandParameter);
            Assert.That(project.ControlPoints[0].IsDisabled, Is.True); Assert.That(project.ControlPoints[1].IsDisabled, Is.False);
            owner.SplitControlPointCommand.Execute(captured); Assert.That(project.ControlPoints, Has.Count.EqualTo(2));
            zone = lane.BringPointIntoView(state.Points[0].Id)!; lane.HandlePointKey(Key.Apps, ModifierKeys.None, zone);
            Assert.That(((MenuItem)lane.ZoneMenu.Items[0]).Header, Is.EqualTo("Enable"));
            lane.ResetInteraction(); Assert.That(lane.MenuSplitRequest, Is.Null); Assert.That(lane.ZoneMenu.IsOpen, Is.False);
        }
        finally { UiTestPump.Run(owner.DeactivateAsync()); }
    }

    [Test]
    public void ZoneUsesOwnerStyleAndTemplateAndExposesPointState()
    {
        var point = new ControlPointSnapshot(Guid.NewGuid(), 0, 4, "Drum", true);
        var lane = OHsControlPointLaneTests.Create([point]); var zone = lane.BringPointIntoView(point.Id)!;
        UiControlContract.AssertStyle<OHsControlPointZone>(zone, "OHsControlPointZoneStyle");
        UiControlContract.AssertTemplate(zone, "OHsControlPointZoneTemplate");
        Assert.That(zone.Label, Does.Contain("Drum").And.Contain("4%"));
        Assert.That(AutomationProperties.GetName(zone), Does.Contain("disabled"));
        Assert.That(zone.IsEnabled, Is.True);
    }

    [Test]
    public void RecycledZoneClearsPreviewAndExposesNewIdentity()
    {
        var point = new ControlPointSnapshot(Guid.NewGuid(), 0, 50, "Normal");
        var lane = OHsControlPointLaneTests.Create([point]); var zone = lane.BringPointIntoView(point.Id)!;
        zone.PreviewVolume = 75; zone.IsVolumeDragging = true;
        zone.Point = new(Guid.NewGuid(), 900, 20, "Soft", true);
        Assert.That(zone.PreviewVolume, Is.Null); Assert.That(zone.IsVolumeDragging, Is.False);
        Assert.That(zone.Label, Does.Contain("Soft").And.Contain("20%"));
        Assert.That(AutomationProperties.GetName(zone), Does.Contain("900").And.Contain("disabled"));
        Assert.That(zone.ToolTip, Is.EqualTo(zone.Label));
    }
}
