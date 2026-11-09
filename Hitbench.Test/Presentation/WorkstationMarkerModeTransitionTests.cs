using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationMarkerModeTransitionTests
{
    [SetUp]
    public void Setup() => new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();

    [Test]
    public void ModeChangesAfterOneSecondHoldAndPreservesOtherLoading()
    {
        var page = WorkstationTestPage.Create(); var vm = (WorkstationPageVM)page.DataContext;
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duration = TimeSpan.Zero;
        page.MarkerModeTransitionDelay = (delay, _) => { duration = delay; return hold.Task; };
        try
        {
            UiTestPump.Run(page.LoadCompletion); view.UpdateLayout();
            var switching = page.ChangeMarkerModeAsync(true);
            Assert.That(duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(page.IsMarkerModeTransitionActive, Is.True);
            Assert.That(page.IsHeaderDimmed, Is.True);
            Assert.That(page.LoadingStatusText, Is.Empty);
            Assert.That(vm.IsStaticMarker, Is.False);
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.True);
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(page.ChangeMarkerModeAsync(false), Is.SameAs(switching));
            page.IsAudioBusy = true; page.AudioBusyText = "Existing operation";
            hold.SetResult(); UiTestPump.Run(switching); view.UpdateLayout();
            Assert.That(vm.IsStaticMarker, Is.True);
            Assert.That(page.IsMarkerModeTransitionActive, Is.False);
            Assert.That(page.IsLoadingPresentationActive, Is.True);
            Assert.That(page.LoadingStatusText, Is.EqualTo("Existing operation"));
            page.IsAudioBusy = false;
            Assert.That(page.IsLoadingPresentationActive, Is.False);
        }
        finally { hold.TrySetResult(); UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void FunctionKeyChangesModeWithoutTabNavigation()
    {
        var page = WorkstationTestPage.Create(); var vm = (WorkstationPageVM)page.DataContext;
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        page.MarkerModeTransitionDelay = (_, _) => Task.CompletedTask;
        try
        {
            UiTestPump.Run(page.LoadCompletion);
            Assert.That(page.HandlePlaybackKey(Key.F8, ModifierKeys.None, false, page), Is.True);
            UiTestPump.Run(page.MarkerModeChangeCompletion);
            Assert.That(vm.IsStaticMarker, Is.True);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [TestCase(false)] [TestCase(true)]
    public void CancellationPreventsLateApplication(bool reload)
    {
        var page = WorkstationTestPage.Create(); var vm = (WorkstationPageVM)page.DataContext;
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.MarkerModeTransitionDelay = (_, _) => hold.Task;
        try
        {
            UiTestPump.Run(page.LoadCompletion);
            var switching = page.ChangeMarkerModeAsync(true);
            if (reload) UiTestPump.Run(vm.LoadAsync("replacement.json", default)); else UiTestPump.Run(page.DeactivateAsync());
            UiTestPump.Run(switching); hold.SetResult(); view.UpdateLayout();
            Assert.That(vm.IsStaticMarker, Is.False);
            Assert.That(page.IsMarkerModeTransitionActive, Is.False);
        }
        finally { hold.TrySetResult(); UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void SelectorUsesExistingSegmentedContractAndNoTabStops()
    {
        var page = WorkstationTestPage.Create();
        try
        {
            UiTestPump.Layout(page, 640, 700);
            var group = (SegmentedToggleGroup)page.Toolbar.FindName("MarkerModeGroup");
            UiControlContract.AssertStyle<SegmentedToggleGroup>(group, "OHsWorkstationMarkerModeStyle");
            var options = group.Items.OfType<SegmentedToggleOption>().ToArray();
            Assert.That(options.Select(o => o.Content), Is.EqualTo(new[] { "MarkerStatic", "MarkerDynamic" }));
            Assert.That(options.Select(o => o.Value), Is.EqualTo(new[] { true, false }));
            foreach (var option in options)
            {
                Assert.That(option.IsTabStop, Is.False);
                Assert.That(System.Windows.Automation.AutomationProperties.GetName(option), Is.Not.Empty);
                Assert.That(option.ToolTip, Is.Not.Null);
            }
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void TransitionRestoresExistingVolumePresentation()
    {
        var page = WorkstationTestPage.Create();
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 700 });
        page.MarkerModeTransitionDelay = (_, _) => Task.CompletedTask;
        try
        {
            UiTestPump.Run(page.LoadCompletion);
            page.ShowVolumeOverlay(false, new Point(), false, false);
            UiTestPump.Run(page.ChangeMarkerModeAsync(true));
            Assert.That(page.IsVolumeOverlayOpen, Is.True);
            Assert.That(page.IsLoadingPresentationActive, Is.False);
            Assert.That(page.VolumeBlurEffect, Is.Not.Null);
            Assert.That(((Grid)page.FindName("WorkstationContent")).Effect, Is.SameAs(page.VolumeBlurEffect));
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }
}
