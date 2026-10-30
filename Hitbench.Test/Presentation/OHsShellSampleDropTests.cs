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
  var page=new WorkstationPage(vm);var window=TestShell.Create(new HomePage(homeVm),messenger,()=>page,homeVm,settings);
  try {UiTestPump.Run(vm.LoadAsync("project.json",default));vm.AcknowledgePresentationReady(vm.LoadGeneration);((ContentControl)window.FindName("PageHost")).Content=page;
   UiTestPump.Run(window.HandleSampleFileDropAsync(new DataObject(typeof(OHsSampleTrackDragData),new OHsSampleTrackDragData(vm.SampleManagementContext!,"a.wav"))));Assert.That(picker.Calls,Is.Zero);
   UiTestPump.Run(window.HandleSampleFileDropAsync(new DataObject(DataFormats.FileDrop,new[]{path})));Assert.That(picker.Calls,Is.EqualTo(1));
   window.ShowPopupOverlay();UiTestPump.Run(window.HandleSampleFileDropAsync(new DataObject(DataFormats.FileDrop,new[]{path})));Assert.That(picker.Calls,Is.EqualTo(1));window.HidePopupOverlay();
  }finally {UiTestPump.Run(page.DeactivateAsync());window.Close();homeVm.Deactivate();File.Delete(path);}
 }
}
