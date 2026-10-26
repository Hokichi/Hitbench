using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class InstrumentalDrawerInputTests
{
    [Test]
    public void PublicViewportBindingAndControlRevealDoNotFeedBack()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(new(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0), [0], [120], TimeSpan.Zero, 4, 32), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        var drawer = new OHsInstrumentalArrangementDrawer(vm) { TimelineContentWidth = 2000, TimelineViewportWidth = 200, TimelineHorizontalOffset = 400 };
        var roll = (PianoRollView)drawer.FindName("PianoRoll");
        drawer.Attach(); UiTestPump.Layout(drawer, 500, 350);
        var requests = new List<double>();
        drawer.HorizontalOffsetRequested += value => { requests.Add(value); drawer.TimelineHorizontalOffset = value; };
        try
        {
            Assert.That(roll.ContentWidth, Is.EqualTo(2000)); Assert.That(roll.ViewportWidth, Is.EqualTo(200)); Assert.That(roll.HorizontalOffset, Is.EqualTo(400));
            roll.SetCurrentValue(PianoRollView.HorizontalOffsetProperty, 600d);
            Assert.That(requests, Is.EqualTo(new[] { 600d }));
            drawer.TimelineHorizontalOffset = 700;
            roll.GetBindingExpression(PianoRollView.HorizontalOffsetProperty)!.UpdateTarget();
            Assert.That(requests.Count, Is.EqualTo(1)); Assert.That(roll.HorizontalOffset, Is.EqualTo(700));
            drawer.Detach(); roll.SetCurrentValue(PianoRollView.HorizontalOffsetProperty, 800d);
            Assert.That(requests.Count, Is.EqualTo(1));
            UiControlContract.AssertStyle<PianoRollView>(roll, "BasePianoRollViewStyle");
        }
        finally { drawer.Detach(); UiTestPump.Run(vm.DeactivateAsync()); }
    }
    [Test]
    public void PianoFocusRoutesTransportOnceAndKeepsEditingKeysLocal()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(shell.Dispatcher));
        var audio = new StubWorkstationAudioService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(owner);
        UiTestPump.Run(owner.LoadAsync("project.json", default)); owner.AcknowledgePresentationReady(owner.LoadGeneration);
        ((ContentControl)shell.FindName("PageHost")).Content = page;
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(new(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0), [0], [120], TimeSpan.Zero, 4, 32), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        var drawer = new OHsInstrumentalArrangementDrawer(vm);
        using var cancel = new CancellationTokenSource();
        var showing = shell.PresentDrawerAsync(drawer, cancel.Token, (_, _) => Task.CompletedTask, true);
        var roll = (PianoRollView)drawer.FindName("PianoRoll");
        try
        {
            var follow = owner.ScrollWithMarker;
            Assert.That(shell.HandleWindowKeyDown(true, Key.F, ModifierKeys.None, false, roll), Is.True);
            Assert.That(owner.ScrollWithMarker, Is.EqualTo(!follow));
            Assert.That(shell.HandleWindowKeyDown(true, Key.F, ModifierKeys.None, true, roll), Is.True);
            Assert.That(owner.ScrollWithMarker, Is.EqualTo(!follow));
            Assert.That(shell.HandleWindowKeyDown(true, Key.F3, ModifierKeys.None, false, roll), Is.False);
            Assert.That(shell.HandleWindowKeyDown(true, Key.F, ModifierKeys.Control, false, roll), Is.False);
            Assert.That(shell.HandleWindowKeyDown(true, Key.F, ModifierKeys.None, false, new TextBox()), Is.False);
            Assert.That(owner.ScrollWithMarker, Is.EqualTo(!follow));
            shell.FlowDirection = FlowDirection.RightToLeft;
            drawer.GetBindingExpression(FrameworkElement.FlowDirectionProperty)!.UpdateTarget();
            Assert.That(drawer.FlowDirection, Is.EqualTo(FlowDirection.RightToLeft));
            Assert.That(shell.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, roll), Is.True);
            UiTestPump.Run(shell.DrawerInputCompletion);
            Assert.That(audio.PlayCalls, Is.EqualTo(1));
            Assert.That(shell.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, true, roll), Is.True);
            Assert.That(audio.PlayCalls, Is.EqualTo(1));
            Assert.That(shell.HandleWindowKeyDown(true, Key.Left, ModifierKeys.None, false, roll), Is.False);
            Assert.That(shell.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, new TextBox()), Is.False);
            owner.IsAdditionMode = false;
            Assert.That(shell.HandleWindowKeyDown(true, Key.D2, ModifierKeys.None, false, roll), Is.True);
            Assert.That(vm.IsAdditionMode, Is.True);
            Assert.That(owner.IsAdditionMode, Is.False);
        }
        finally { cancel.Cancel(); UiTestPump.Run(showing); UiTestPump.Run(vm.DeactivateAsync()); UiTestPump.Run(page.DeactivateAsync()); shell.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
