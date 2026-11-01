using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Test.Presentation;
public sealed class ManageSamplesDragTestContext : IDisposable
{
 public ManageSamplesPopupVM Vm {get;}
 public ManageSamplesDrawer Drawer {get;}
 public AdornerDecorator Host {get;}
 public Grid Root {get;} = new();
 public SampleManagementEntry Entry => Vm.Entries[0];
 public ManageSamplesDragTestContext()
 {
  new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
  var files=new StubSampleFileService(new[]{"a.wav","b.wav"});
  Vm=new(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService([]),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler());
  UiTestPump.Run(Vm.InitializeAsync(default)); Drawer=new(Vm); Root.Children.Add(Drawer); Host=new(){Child=Root};
  Drawer.SetSampleDragPointerProvider(_=>new Point(100,100)); UiTestPump.Layout(Host,800,500);
  foreach (var element in Children(Root).OfType<FrameworkElement>().ToArray()) element.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, element));
 }
 public OHsSampleRow RowFor(SampleManagementEntry entry) => Children(Root).OfType<OHsSampleRow>().Single(r=>ReferenceEquals(r.DataContext,entry));
 private static IEnumerable<DependencyObject> Children(DependencyObject root) {yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children(VisualTreeHelper.GetChild(root,i)))yield return child;}
 public void Dispose(){Drawer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));UiTestPump.Run(Vm.DeactivateAsync());}
}
