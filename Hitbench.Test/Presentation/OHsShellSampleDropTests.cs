using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;
using Hitbench.Resources.DataModels;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public sealed class OHsShellSampleDropTests
{
 [Test] public void ShellRoutesOnlyExternalFilesAndRejectsModalDrops()
 {
  new SongsResponsiveLayoutTests().SetUp();var source=Path.GetTempFileName();var path=Path.ChangeExtension(source,".wav");File.Move(source,path);
  var messenger=new WeakReferenceMessenger();var settings=new StubAppSettingsService();var homeVm=new HomePageVM(new StubHomeFolderService(),settings,new StubInstallationDialogService(),new ImmediateUiScheduler(),new StubProjectFileService((_,_,_)=>Task.FromResult("saved.json")),messenger);
  var picker=new StubOHsSampleDropImportDialogService {Select=(_,_)=>Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>())};
  var vm=new WorkstationPageVM(new StubWorkstationSourceService(),new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),MarkerPlaybackCoordinator.DelayAsync),new ImmediateUiScheduler(),new StubSampleFileService(new[]{"a.wav"}),new StubSampleFileDialogService(Array.Empty<string>()),messenger:messenger,sampleDropDialog:picker);
  var errors = new List<string>();
  var page=new WorkstationPage(vm);var window=TestShell.Create(new HomePage(homeVm),messenger,()=>page,homeVm,settings, reportError: errors.Add);
  Assert.That(window.Content,Is.TypeOf<System.Windows.Documents.AdornerDecorator>());
  Assert.That(((System.Windows.Documents.AdornerDecorator)window.Content).Child,Is.SameAs(window.FindName("ShellRoot")));
  var previousContext = SynchronizationContext.Current;
  SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
  try {UiTestPump.Run(vm.LoadAsync("project.json",default));vm.AcknowledgePresentationReady(vm.LoadGeneration);((ContentControl)window.FindName("PageHost")).Content=page;
   UiTestPump.Run(window.HandleSampleFileDropAsync(new DataObject(typeof(OHsSampleTrackDragData),new OHsSampleTrackDragData(vm.SampleManagementContext!,"a.wav"))));Assert.That(picker.Calls,Is.Zero);
   foreach (var failFormat in new[] { false, true })
       UiTestPump.Run(window.HandleSampleFileDropAsync(new FaultingFileDropDataObject(new DataObject(DataFormats.FileDrop, new[] { path }), failFormat)));
   Assert.That(errors, Is.EqualTo(new[] { "drop data failed", "drop format failed" }));
   for (var cycle = 0; cycle < 3; cycle++) UiTestPump.Run(window.HandleSampleFileDropAsync(new DataObject(DataFormats.FileDrop,new[]{path})));
   Assert.That(picker.Calls,Is.EqualTo(3));
   window.ShowPopupOverlay();UiTestPump.Run(window.HandleSampleFileDropAsync(new DataObject(DataFormats.FileDrop,new[]{path})));Assert.That(picker.Calls,Is.EqualTo(3));window.HidePopupOverlay();
  }finally {UiTestPump.Run(page.DeactivateAsync());window.Close();homeVm.Deactivate();File.Delete(path);SynchronizationContext.SetSynchronizationContext(previousContext);}
 }
}
