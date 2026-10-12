using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public class KeysMappingDrawerIntegrationTests
{
 [Test] public void KeyboardNavigationCanReachLastVirtualizedKey()
 {
  new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
  var ctx=new InstrumentalTrackContext("project.json",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  var vm=new KeysMappingDrawerVM(new(ctx,"Piano",100,new Dictionary<string,string>(),[],[],0),new WeakReferenceMessenger(),new ImmediateUiScheduler(),new StubSampleFileService([]),new StubSampleFileDialogService((string?)null));
  var drawer=new OHsKeysMappingDrawer(vm);
  try {
   drawer.Measure(new Size(600,400));drawer.Arrange(new Rect(0,0,600,400));drawer.UpdateLayout();
   var rows=(ItemsControl)drawer.FindName("KeyRows");
   Assert.That(drawer.HandleNavigationKey(Key.End,ModifierKeys.Alt),Is.True);
   Assert.That(((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(107)).DataContext,Is.TypeOf<KeyMappingEntry>());
   Assert.That(((KeyMappingEntry)((FrameworkElement)rows.ItemContainerGenerator.ContainerFromIndex(107)).DataContext).Key,Is.EqualTo("B9"));
  } finally {UiTestPump.Run(vm.DeactivateAsync());}
 }
}
