using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Soundie.Core.Entities;
using Soundie.ViewModels.Shell;
using Soundie.Views.Pages;
namespace Soundie.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationTrackRowTests
{
    [Test]
    public void TrackIdentityFollowsReorderedAndRecycledContainers()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        foreach (var index in Enumerable.Range(0, 9)) vm.Tracks.Add(new Track { Name = $"Track {index}" });
        var list = new OHsWorkstationTrackList { DataContext = vm };
        using var view = new InMemoryView(new Window { Content = list, Width = 900, Height = 180 });
        var items = (ListBox)list.FindName("TrackList");
        var last = vm.Tracks[8];
        try
        {
            items.ScrollIntoView(last); view.UpdateLayout();
            Assert.That(Rows(items).Single(row => ReferenceEquals(row.Track, last)).TrackColorIndex, Is.EqualTo(8));
            vm.Tracks.Move(8, 1); items.ScrollIntoView(last); view.UpdateLayout();
            Assert.That(Rows(items).Single(row => ReferenceEquals(row.Track, last)).TrackColorIndex, Is.EqualTo(1));
            items.ScrollIntoView(vm.Tracks[^1]); view.UpdateLayout();
            foreach (var row in Rows(items))
                Assert.That(row.TrackColorIndex, Is.EqualTo(vm.Tracks.IndexOf(row.Track!)));
        }
        finally { list.Deactivate(); UiTestPump.Run(vm.DeactivateAsync()); }

        static IEnumerable<OHsWorkstationTrackRow> Rows(DependencyObject element)
        {
            if (element is OHsWorkstationTrackRow row) { yield return row; yield break; }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                foreach (var child in Rows(VisualTreeHelper.GetChild(element, i))) yield return child;
        }
    }

    [Test]
    public void ContextMenuInputUsesCurrentTrackAndNameContent()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        UiTestPump.Run(vm.LoadAsync("project.json", default));
        vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.AddPianoTrackCommand.Execute(null);
        vm.AddPianoTrackCommand.Execute(null);
        var requests = new List<Soundie.Resources.CustomControls.BalloonComboBox>();
        var row = new OHsWorkstationTrackRow(requests.Add) { Track = vm.Tracks[0], ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        using var source = new InMemoryPresentationSource();
        try
        {
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent };
            row.RaiseEvent(press);
            Assert.That(press.Handled, Is.True, "Track menu press must not reach a destructive child right-click handler.");
            var pointer = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent };
            row.RaiseEvent(pointer);
            Assert.That(pointer.Handled, Is.True);
            Assert.That(requests.Last().ContextMenu.DataContext, Is.SameAs(vm.Tracks[0]));
            var old = requests.Last().ContextMenu;
            row.Track = vm.Tracks[1]; view.UpdateLayout();
            row.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Apps) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.That(requests.Count, Is.EqualTo(2));
            Assert.That(requests.Last().ContextMenu.DataContext, Is.SameAs(vm.Tracks[1]));
            Assert.That(old.IsEnabled, Is.False);
            var input = FindNameText(row)!;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Apps) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.That(requests.Count, Is.EqualTo(3));
            Assert.That(requests.Last().ContextMenu.IsOpen, Is.False);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    [Test]
    public void AppearanceMenuCommitsAndCancelsForCurrentTrack()
    {
        new VolumeWheelTests().SetUp();
        Soundie.Resources.DataModels.OHsTrackAppearanceResult? result = null;
        var requested = new List<string>();
        var dialog = new StubOHsTrackAppearanceDialogService { Request = (request, _) =>
        {
            requested.Add(request.Name);
            return Task.FromResult(result);
        }};
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new Soundie.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), trackAppearanceDialog: dialog);
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.AddPianoTrackCommand.Execute(null); var track = vm.Tracks.Single(); track.Name = "Original";
        var row = new OHsWorkstationTrackRow { Track = track, ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        try
        {
            var trigger = (Soundie.Resources.CustomControls.BalloonComboBox)row.GetRemovalFocusTarget()!;
            var action = (MenuItem)trigger.ContextMenu!.Items[2];
            Assert.That(action.Command, Is.SameAs(vm.ChangeTrackNameAndColorCommand));
            Assert.That(action.CommandParameter, Is.SameAs(track));
            var command = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)action.Command;
            UiTestPump.Run(command.ExecuteAsync(action.CommandParameter));
            Assert.That(track.Name, Is.EqualTo("Original"));
            result = new("Accepted", "#123456");
            UiTestPump.Run(command.ExecuteAsync(action.CommandParameter)); view.UpdateLayout();
            Assert.That(FindNameText(row)!.Text, Is.EqualTo("Accepted"));
            Assert.That(track.Color, Is.EqualTo("#123456"));
            result = null;
            UiTestPump.Run(command.ExecuteAsync(action.CommandParameter));
            Assert.That(track.Name, Is.EqualTo("Accepted"));
            Assert.That(track.Color, Is.EqualTo("#123456"));
            Assert.That(requested, Is.EqualTo(new[] { "Original", "Original", "Accepted" }));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    [Test]
    public void TimelineRightButtonPhasesBypassTrackMenu()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        var requests = 0;
        var row = new OHsWorkstationTrackRow(_ => requests++) { Track = new Track(), ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        try
        {
            foreach (var mode in new[] { false, true })
            {
                vm.IsAdditionMode = mode;
                foreach (var routedEvent in new[] { UIElement.PreviewMouseRightButtonDownEvent, UIElement.PreviewMouseRightButtonUpEvent })
                {
                    var input = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = routedEvent };
                    row.SampleTimeline!.RaiseEvent(input);
                    Assert.That(input.Handled, Is.False);
                    Assert.That(requests, Is.Zero);
                }
            }
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    private static TextBlock? FindNameText(DependencyObject root)
    {
        if (root is TextBlock { Name: "SampleTrackName" } input) return input;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindNameText(VisualTreeHelper.GetChild(root, i)) is {} match) return match;
        return null;
    }
    [Test]
    public void NameEditorUsesDraftAndDisplaysAcceptedCommit()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.AddPianoTrackCommand.Execute(null); var track = vm.Tracks.Single(); track.Name = "Original";
        var row = new OHsWorkstationTrackRow { Track = track, ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        vm.BeginTrackRenameCommand.Execute(track); view.UpdateLayout();
        row.TrackNameDraft = "Accepted";
        Assert.That(vm.TrackNameDraft, Is.EqualTo("Accepted")); Assert.That(track.Name, Is.EqualTo("Original"));
        UiTestPump.Run(vm.CommitTrackRenameAsync(track)); view.UpdateLayout();
        Assert.That(track.Name, Is.EqualTo("Accepted"));
        Assert.That(row.TrackNameDraft, Is.EqualTo("Accepted"));
        vm.BeginTrackRenameCommand.Execute(track); row.TrackNameDraft = "Cancelled"; vm.CancelTrackRename(track);
        Assert.That(row.TrackNameDraft, Is.EqualTo("Accepted"));
        UiTestPump.Run(vm.DeactivateAsync());
    }
    [Test]
    public void RecyclingDetachesOldMenuAndCapturesTrackIdentity()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.AddPianoTrackCommand.Execute(null); vm.AddPianoTrackCommand.Execute(null);
        var a = vm.Tracks[0]; var b = vm.Tracks[1];
        var row = new OHsWorkstationTrackRow { Track = a, ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        var trigger = (Soundie.Resources.CustomControls.BalloonComboBox)row.GetRemovalFocusTarget()!;
        UiControlContract.AssertStyle<Soundie.Resources.CustomControls.BalloonComboBox>(trigger, "OHsWorkstationTrackActionsStyle");
        var old = trigger.ContextMenu!;
        Assert.That(old.DataContext, Is.SameAs(a));
        Assert.That(((MenuItem)old.Items[0]).CommandParameter, Is.SameAs(a));
        Assert.That(vm.MoveTrackUpCommand.CanExecute(a), Is.False);
        row.Track = b; view.UpdateLayout();
        Assert.That(trigger.ContextMenu, Is.Not.SameAs(old)); Assert.That(old.IsEnabled, Is.False);
        Assert.That(trigger.ContextMenu!.DataContext, Is.SameAs(b));
        Assert.That(vm.MoveTrackUpCommand.CanExecute(b), Is.True);
        UiTestPump.Run(vm.DeactivateAsync());
    }
    [Test]
    public void TrackStateAndNameInputFollowCurrentOwner()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.AddPianoTrackCommand.Execute(null);
        var track = vm.Tracks.Last();
        var row = new OHsWorkstationTrackRow { Track = track, ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        vm.SetTrackCollapsed(track, true); row.RefreshPresentation();
        Assert.That(row.IsTrackCollapsed, Is.True);
        vm.BeginTrackRenameCommand.Execute(track); row.RefreshPresentation();
        Assert.That(row.IsRenaming, Is.True);
        row.Track = new Track { Name = "Other" }; view.UpdateLayout();
        Assert.That(row.IsRenaming, Is.False);
        Assert.That(vm.RenamingTrack, Is.Null);
        UiTestPump.Run(vm.DeactivateAsync());
    }
    [Test]
    public void RebindingKeepsLiveTrackAndRestoresSampleChoice()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        vm.SampleFiles.Add("a.wav"); vm.SampleFiles.Add("b.wav");
        var a = new Track { Name = "A", SelectedSampleFileName = "a.wav" };
        var b = new Track { Name = "B", SelectedSampleFileName = "b.wav" };
        var row = new OHsWorkstationTrackRow { Track = a, ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        row.Track = b; view.UpdateLayout(); row.RefreshPresentation();
        Assert.That(row.Track, Is.SameAs(b));
        var template = (DataTemplate)row.FindResource("OHsWorkstationSampleTrackTemplate");
        var selector = (ComboBox)template.FindName("TrackSampleFileSelector", row.RowContent);
        Assert.That(selector.SelectedItem, Is.EqualTo("b.wav"));
        Assert.That(row.SampleTimeline!.DataContext, Is.SameAs(b));
        Assert.That(a.Name, Is.EqualTo("A"));
        row.Track = a; view.UpdateLayout(); row.RefreshPresentation();
        Assert.That(row.SampleTimeline!.DataContext, Is.SameAs(a));
        Assert.That(selector.SelectedItem, Is.EqualTo("a.wav"));
        UiTestPump.Run(vm.DeactivateAsync());
    }
}
