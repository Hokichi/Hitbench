using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class InstrumentalTrackCompositionTests
{
    [Test]
    public void InstrumentRowHasArrangementAndMappingInsteadOfSampleSelectors()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create(); ((Hitbench.ViewModels.Shell.WorkstationPageVM)page.DataContext).Tracks.Add(new InstrumentalTrack());
        UiTestPump.Layout(page, 1100, 700);
        var list = (ListBox)((Hitbench.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.That(Children(row).OfType<ComboBox>(), Is.Empty);
        var arrangement = Children(row).OfType<Hitbench.Resources.CustomControls.BalloonButton>().Single(b => b.ButtonText == "View Arrangement");
        UiControlContract.AssertStyle<Hitbench.Resources.CustomControls.BalloonButton>(arrangement, "OHsWorkstationInstrumentMutedActionStyle");
        Assert.That(arrangement.Style.BasedOn, Is.SameAs(arrangement.FindResource("OHsBalloonSurfaceActionStyle")));
        var trigger = Children(row).OfType<Hitbench.Resources.CustomControls.BalloonComboBox>().Single();
        trigger.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var mapping = trigger.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Keys Mapping"));
        UiControlContract.AssertStyle<MenuItem>(mapping, "OHsWorkstationKeysMappingMenuItemStyle");
        Assert.That(Children(row).OfType<Hitbench.Resources.CustomControls.BeatGridView>(), Is.Empty);
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
            Assert.That(popup.HandleEditorShortcut(Key.Space, ModifierKeys.None, false, new TextBox()), Is.False); Assert.That(popup.FindName("PianoRoll"), Is.TypeOf<Hitbench.Resources.CustomControls.PianoRollView>());
            UiControlContract.AssertStyle<Hitbench.Resources.CustomControls.PianoRollView>((Hitbench.Resources.CustomControls.PianoRollView)popup.FindName("PianoRoll"), "BasePianoRollViewStyle");
        }
        finally { popup.Detach(); UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
