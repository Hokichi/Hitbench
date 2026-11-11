using Hitbench.Test;
using System.Windows;
using System.Windows.Automation;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationToolbarHistoryTests
{
    [TestCase(640, FlowDirection.LeftToRight)] [TestCase(1200, FlowDirection.RightToLeft)]
    public void ToolbarActionsFollowExistingHistoryAndResourceContracts(double width, FlowDirection direction)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var track = new Track { SelectedSampleFileName = "a.wav" };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "audio",
            new ProjectData { UninheritedPoints = TimingTestData.Points([0], [120]), Tracks = [track] })) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        var page = new WorkstationPage(vm) { FlowDirection = direction };
        using var view = new InMemoryView(new Window { Content = page, Width = width, Height = 700 });
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration); view.UpdateLayout();
            var undo = (BalloonButton)page.Toolbar.FindName("UndoButton");
            var redo = (BalloonButton)page.Toolbar.FindName("RedoButton");
            Assert.That(undo, Is.Not.Null); Assert.That(redo, Is.Not.Null);
            foreach (var (button, icon) in new[] { (undo, "RegularUndo"), (redo, "RegularRedo") })
            {
                UiControlContract.AssertStyle<BalloonButton>(button, "OHsWorkstationTransportButtonStyle");
                Assert.That(button.ButtonIcon, Is.SameAs(button.FindResource(icon)));
                Assert.That(button.IsTabStop, Is.False);
                Assert.That(AutomationProperties.GetName(button), Is.Not.Empty);
                Assert.That(button.ToolTip, Is.Not.Null);
                Assert.That(button.IsEnabled, Is.False);
            }
            Assert.That(undo.Command, Is.SameAs(vm.UndoCommand)); Assert.That(redo.Command, Is.SameAs(vm.RedoCommand));
            vm.IsAdditionMode = true; vm.PlaceSample(track, 125); view.UpdateLayout();
            Assert.That(undo.IsEnabled, Is.True); Assert.That(redo.IsEnabled, Is.False);
            undo.Command.Execute(null); view.UpdateLayout();
            Assert.That(track.Samples, Is.Empty); Assert.That(redo.IsEnabled, Is.True);
            redo.Command.Execute(null); view.UpdateLayout(); Assert.That(track.Samples, Has.Count.EqualTo(1));
            undo.Command.Execute(null); vm.PlaceSample(track, 250); view.UpdateLayout(); Assert.That(redo.IsEnabled, Is.False);
            vm.IsReturning = true; view.UpdateLayout(); Assert.That(undo.IsEnabled, Is.False); Assert.That(redo.IsEnabled, Is.False);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }
}
