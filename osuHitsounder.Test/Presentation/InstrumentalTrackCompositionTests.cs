using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Helpers;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class InstrumentalTrackCompositionTests
{
    [Test]
    public void InstrumentRowHasArrangementAndMappingInsteadOfSampleSelectors()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create(); ((osuHitsounder.ViewModels.Shell.WorkstationPageVM)page.DataContext).Tracks.Add(new InstrumentalTrack());
        UiTestPump.Layout(page, 1100, 700);
        var list = (ListBox)((osuHitsounder.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.That(Children(row).OfType<ComboBox>(), Is.Empty);
        var arrangement = Children(row).OfType<osuHitsounder.Resources.CustomControls.BalloonButton>().Single(b => b.ButtonText == "View Arrangement");
        UiControlContract.AssertStyle<osuHitsounder.Resources.CustomControls.BalloonButton>(arrangement, "OHsWorkstationInstrumentActionStyle");
        var mapping = Children(row).OfType<osuHitsounder.Resources.CustomControls.BalloonButton>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Keys Mapping");
        UiControlContract.AssertStyle<osuHitsounder.Resources.CustomControls.BalloonButton>(mapping, "OHsWorkstationInstrumentActionStyle");
        Assert.That(Children(row).OfType<osuHitsounder.Resources.CustomControls.BeatGridView>(), Is.Empty);
        UiTestPump.Run(page.DeactivateAsync());
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    { yield return root; for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child; }
    [Test]
    public void SelectorChoosesInstrumentTemplate()
    {
        var sample = new DataTemplate(); var instrument = new DataTemplate();
        var selector = new WorkstationTrackTemplateSelector { SampleTrackTemplate = sample, InstrumentalTrackTemplate = instrument };
        Assert.That(selector.SelectTemplate(new InstrumentalTrack(), new ContentControl()), Is.SameAs(instrument));
        Assert.That(selector.SelectTemplate(new Track(), new ContentControl()), Is.SameAs(sample));
    }
    [Test]
    public void PopupSpaceDoesNotConsumeTextInput()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var ctx = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new InstrumentalTrackSnapshot(ctx, "Piano", 100, new Dictionary<string, string>(), [], [], 0);
        var vm = new InstrumentalArrangementPopupVM(new(snapshot, "song", new(TimeSpan.FromSeconds(10), [.5f]), [0], [120], TimeSpan.Zero, 4, 32, 100, 100, 100), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        var popup = new OHsInstrumentalArrangementPopup(vm);
        try
        {
            Assert.That(popup.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, new TextBox()), Is.False); Assert.That(popup.FindName("PianoRoll"), Is.TypeOf<osuHitsounder.Resources.CustomControls.PianoRollView>());
            var scroller = (ScrollViewer)popup.FindName("WaveformScroll"); scroller.ApplyTemplate();
            Assert.That(scroller.Template, Is.SameAs(popup.FindResource("DefaultScrollViewerTemplate")));
        }
        finally { popup.Close(); UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
