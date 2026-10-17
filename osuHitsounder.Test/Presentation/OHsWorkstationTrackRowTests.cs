using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationTrackRowTests
{
    [Test]
    public void EditorInputEventsCommitAndCancelDraft()
    {
        new VolumeWheelTests().SetUp();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        vm.AddPianoTrackCommand.Execute(null); var track = vm.Tracks.Single(); track.Name = "Original";
        var row = new OHsWorkstationTrackRow { Track = track, ViewModel = vm };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 300 });
        var input = FindNameInput(row)!;
        Assert.That(input.IsReadOnly, Is.True);
        vm.BeginTrackRenameCommand.Execute(track); view.UpdateLayout();
        Assert.That(input.IsReadOnly, Is.False);
        input.SetCurrentValue(TextBox.TextProperty, "Typed");
        Assert.That(track.Name, Is.EqualTo("Original"));
        using var source = new InMemoryPresentationSource();
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.That(track.Name, Is.EqualTo("Typed"));
        vm.BeginTrackRenameCommand.Execute(track); input.SetCurrentValue(TextBox.TextProperty, "Cancelled");
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.That(track.Name, Is.EqualTo("Typed")); Assert.That(input.IsReadOnly, Is.True);
        vm.BeginTrackRenameCommand.Execute(track); input.SetCurrentValue(TextBox.TextProperty, "Focus loss");
        input.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, input, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Assert.That(track.Name, Is.EqualTo("Focus loss"));
        UiTestPump.Run(vm.DeactivateAsync());
    }
    private static TextBox? FindNameInput(DependencyObject root)
    {
        if (root is TextBox input) return input;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindNameInput(VisualTreeHelper.GetChild(root, i)) is {} match) return match;
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
        var trigger = (osuHitsounder.Resources.CustomControls.BalloonComboBox)row.GetRemovalFocusTarget()!;
        UiControlContract.AssertStyle<osuHitsounder.Resources.CustomControls.BalloonComboBox>(trigger, "OHsWorkstationTrackActionsStyle");
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
