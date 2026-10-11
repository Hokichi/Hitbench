using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class ManageSamplesPopupIntegrationTests
{
    [Test] public void RowPlayBindsToPreviewAndRowUsesHoverContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files=new StubSampleFileService(["hit.wav"]); var preview=new StubSamplePreviewService();
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new osuHitsounder.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher),preview);
        var popup=new ManageSamplesPopup(vm);
        try
        {
            UiTestPump.Run(popup.InitializeAsync(default)); using var view=new InMemoryView(popup,scheduleLayout:false);
            var play=Descendants(view.Root).OfType<BalloonButton>().Single(b=>b.ButtonText=="Play");
            Assert.That(play.Command,Is.SameAs(vm.PlayCommand)); Assert.That(play.CommandParameter,Is.SameAs(vm.Entries.Single()));
            Assert.That(play.ButtonIcon,Is.SameAs(popup.FindResource("MediaPlay")));
            UiTestPump.Run(vm.PlayCommand.ExecuteAsync(play.CommandParameter)); Assert.That(preview.Playing,Is.True);
            Assert.That(play.ButtonIcon,Is.SameAs(popup.FindResource("MediaPause")));
            Assert.That(play.ButtonText,Is.EqualTo("Pause"));
            Assert.That(Descendants(view.Root).OfType<BalloonButton>().Single(b=>b.ButtonText=="Edit").IsEnabled,Is.True);
            Assert.That(Descendants(view.Root).OfType<BalloonButton>().Single(b=>b.ButtonText=="Delete").IsEnabled,Is.True);
            var row=Descendants(view.Root).OfType<System.Windows.Controls.Border>().Single(b=>ReferenceEquals(b.DataContext,vm.Entries.Single()) && b.Style==popup.FindResource("OHsManageSamplesRowStyle"));
            Assert.That(row.Style.BasedOn,Is.SameAs(popup.FindResource("BaseBorderStyle")));
        }
        finally { UiTestPump.Run(popup.DeactivateAsync()); popup.Close(); }
    }
    [Test] public void LongCatalogUsesVirtualizedContainers()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var names = Enumerable.Range(0,2000).Select(i=>$"sample{i}.wav").ToArray(); var files=new StubSampleFileService(names);
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new osuHitsounder.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var popup=new ManageSamplesPopup(vm);
        try
        {
            UiTestPump.Run(popup.InitializeAsync(default)); using var view=new InMemoryView(popup,scheduleLayout:false);
            var list=(System.Windows.Controls.ItemsControl)popup.FindName("SampleRows");
            Assert.That(list.Items.Count,Is.EqualTo(2000));
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(1999),Is.Null);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0),Is.Not.Null);
        }
        finally { UiTestPump.Run(popup.DeactivateAsync()); popup.Close(); }
    }
    [Test] public void RowEditUpdatesDraftThroughRealBinding()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService(["hit.wav"]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files), new StubSampleFileDialogService(Array.Empty<string>()),
            new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new osuHitsounder.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var popup = new ManageSamplesPopup(vm);
        try
        {
            UiTestPump.Run(popup.InitializeAsync(default)); using var view = new InMemoryView(popup, scheduleLayout:false);
            var edit = Descendants(view.Root).OfType<BalloonButton>().Single(b => b.ButtonText == "Edit");
            Assert.That(edit.Command, Is.SameAs(vm.EditCommand));
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(edit.CommandParameter)); view.UpdateLayout();
            var input = Descendants(view.Root).OfType<System.Windows.Controls.TextBox>().Single();
            input.Text = "kick"; Assert.That(vm.DraftStem, Is.EqualTo("kick"));
            vm.CancelEditCommand.Execute(null); Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav"));
        }
        finally { UiTestPump.Run(popup.DeactivateAsync()); popup.Close(); }
    }
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    { yield return root; for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++) foreach(var c in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root,i))) yield return c; }
    [Test] public void AddActionAndRowsBindToManagerCommands()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService([]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new osuHitsounder.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var popup = new ManageSamplesPopup(vm);
        try
        {
            UiTestPump.Run(popup.InitializeAsync(default)); using var view = new InMemoryView(popup, scheduleLayout: false);
            var add = (BalloonButton)popup.FindName("AddNewSampleButton");
            Assert.That(add.Command, Is.SameAs(vm.AddNewSampleCommand));
            Assert.That(add.ButtonIcon, Is.SameAs(popup.FindResource("PlusSolid")));
            Assert.That(add.IsEnabled, Is.True);
        }
        finally { UiTestPump.Run(popup.DeactivateAsync()); popup.Close(); }
    }
}
