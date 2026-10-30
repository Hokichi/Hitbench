using System.Windows;
using System.Windows.Controls;
using Hitbench.Resources.DataModels;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class WorkstationPianoExpansionTests
{
    [TestCase(false)] [TestCase(true)]
    public void ClosingExpandedDrawerRestoresSavedTrackOffset(bool replaceImmediately)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create();
        var vm = (Hitbench.ViewModels.Shell.WorkstationPageVM)page.DataContext;
        for (var i = 0; i < 80; i++) vm.Tracks.Add(new Hitbench.Core.Entities.Track { Name = $"Track {i}" });
        var tracks = (OHsWorkstationTrackList)page.FindName("TrackListComponent");
        var host = new Grid();
        using var view = new InMemoryView(new Window { Content = page, Width = 1200, Height = 800 });
        try
        {
            tracks.RestoreVerticalOffset(120);
            view.UpdateLayout();
            var offset = tracks.CaptureVerticalOffset();
            Assert.That(offset, Is.GreaterThan(0));
            page.AttachDrawerHost(host, OHsWorkstationDrawerKind.Arrangement);
            page.SettleDrawerHostAnimation(true);
            page.TogglePianoRollExpansion();
            view.UpdateLayout();
            tracks.RestoreVerticalOffset(0);
            page.DetachDrawerHost(host);
            if (replaceImmediately)
            {
                page.AttachDrawerHost(host, OHsWorkstationDrawerKind.Samples);
                page.SettleDrawerHostAnimation(true);
            }
            UiTestPump.Run(page.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
            view.UpdateLayout();
            Assert.That(tracks.CaptureVerticalOffset(), Is.EqualTo(offset));
        }
        finally { page.DetachDrawerHost(host); UiTestPump.Run(page.DeactivateAsync()); }
    }

    [TestCase(0)] [TestCase(240)] [TestCase(600)]
    public void ExpandedAllocationTracksCurrentBudget(double budget)
    {
        var dock = new Size(1200, 800);
        var tracks = new Size(900, budget);
        Assert.That(WorkstationPage.CalculateDrawerHostSize(OHsWorkstationDrawerKind.Arrangement,
            dock, tracks, 200, 0, true).Height, Is.EqualTo(budget));
        Assert.That(WorkstationPage.CalculateDrawerHostSize(OHsWorkstationDrawerKind.Arrangement,
            dock, tracks, 200, 0).Height, Is.EqualTo(budget / 2));
        foreach (var kind in new[] { OHsWorkstationDrawerKind.Samples, OHsWorkstationDrawerKind.KeysMapping })
            Assert.That(WorkstationPage.CalculateDrawerHostSize(kind, dock, tracks, 200, 120, true),
                Is.EqualTo(WorkstationPage.CalculateDrawerHostSize(kind, dock, tracks, 200, 120)));
    }

    [TestCase(FlowDirection.LeftToRight)] [TestCase(FlowDirection.RightToLeft)]
    public void ToggleRestoresViewportWithoutChangingTrackCollapseState(FlowDirection direction)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create();
        page.FlowDirection = direction;
        var vm = (Hitbench.ViewModels.Shell.WorkstationPageVM)page.DataContext;
        var track = new Hitbench.Core.Entities.Track { IsCollapsed = true };
        vm.Tracks.Add(track);
        var host = new Grid();
        var card = (Border)page.FindName("TracksCard");
        try
        {
            UiTestPump.Layout(page, 1200, 800);
            page.AttachDrawerHost(host, OHsWorkstationDrawerKind.Arrangement);
            page.SettleDrawerHostAnimation(true);
            page.TogglePianoRollExpansion();
            Assert.That(page.IsPianoRollExpanded, Is.True);
            Assert.That(card.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(track.IsCollapsed, Is.True);
            page.TogglePianoRollExpansion();
            Assert.That(card.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(page.IsPianoRollExpanded, Is.False);
            page.TogglePianoRollExpansion();
            page.DetachDrawerHost(host);
            Assert.That(card.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(page.IsPianoRollExpanded, Is.False);
            page.AttachDrawerHost(host, OHsWorkstationDrawerKind.Samples);
            page.TogglePianoRollExpansion();
            Assert.That(page.IsPianoRollExpanded, Is.False);
        }
        finally { page.DetachDrawerHost(host); UiTestPump.Run(page.DeactivateAsync()); }
    }
}
