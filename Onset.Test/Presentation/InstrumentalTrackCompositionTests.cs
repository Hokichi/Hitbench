using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Onset.Core.Entities;
using Onset.Resources.DataModels;
using Onset.Resources.Helpers;
using Onset.Services;
using Onset.ViewModels.Popups;
using Onset.Views.Popups;
namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class InstrumentalTrackCompositionTests
{
    [Test]
    public void InstrumentRowHasArrangementAndMappingInsteadOfSampleSelectors()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create(); ((Onset.ViewModels.Shell.WorkstationPageVM)page.DataContext).Tracks.Add(new InstrumentalTrack());
        UiTestPump.Layout(page, 1100, 700);
        var list = (ListBox)((Onset.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.That(Children(row).OfType<ComboBox>(), Is.Empty);
        var arrangement = Children(row).OfType<Onset.Resources.CustomControls.BalloonButton>().Single(b => b.ButtonText == "View Arrangement");
        UiControlContract.AssertStyle<Onset.Resources.CustomControls.BalloonButton>(arrangement, "OHsWorkstationInstrumentMutedActionStyle");
        Assert.That(arrangement.Style.BasedOn, Is.SameAs(arrangement.FindResource("OHsWorkstationInstrumentActionStyle")));
        var mapping = Children(row).OfType<Onset.Resources.CustomControls.BalloonButton>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Keys Mapping");
        UiControlContract.AssertStyle<Onset.Resources.CustomControls.BalloonButton>(mapping, "OHsWorkstationInstrumentActionStyle");
        Assert.That(Children(row).OfType<Onset.Resources.CustomControls.BeatGridView>(), Is.Empty);
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
        var vm = new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(snapshot, [0], [120], TimeSpan.Zero, 4, 32), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        var popup = new OHsInstrumentalArrangementDrawer(vm);
        try
        {
            Assert.That(popup.HandleEditorShortcut(Key.Space, ModifierKeys.None, false, new TextBox()), Is.False); Assert.That(popup.FindName("PianoRoll"), Is.TypeOf<Onset.Resources.CustomControls.PianoRollView>());
            UiControlContract.AssertStyle<Onset.Resources.CustomControls.PianoRollView>((Onset.Resources.CustomControls.PianoRollView)popup.FindName("PianoRoll"), "BasePianoRollViewStyle");
        }
        finally { popup.Detach(); UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
