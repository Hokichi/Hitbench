using CommunityToolkit.Mvvm.Messaging;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class ManageSamplesGroupStateTests
{
    [Test]
    public void EmptyGroupsReactAndSkinVisibilityPreservesExpansion()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService([]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        try
        {
            var drawer = new ManageSamplesDrawer(vm);
            vm.AllowDefaults = true;
            UiTestPump.Layout(drawer, 500, 600);
            var messages = Children(drawer).OfType<TextBlock>().Where(t => t.Text == "No samples found").ToArray();
            Assert.That(messages, Has.Length.EqualTo(3));
            Assert.That(messages.All(t => t.Visibility == Visibility.Visible), Is.True);
            vm.MapsetEntries.Add(new("Mapset/drum-hitclap.wav"));
            UiTestPump.Layout(drawer, 500, 600);
            Assert.That(messages.Count(t => t.Visibility == Visibility.Visible), Is.EqualTo(2));
            vm.MapsetEntries.Clear();
            UiTestPump.Layout(drawer, 500, 600);
            Assert.That(messages.All(t => t.Visibility == Visibility.Visible), Is.True);
            var skin = (Expander)drawer.FindName("SkinDefaultsExpander");
            skin.IsExpanded = false;
            vm.AllowDefaults = false;
            Assert.That(skin.Visibility, Is.EqualTo(Visibility.Collapsed));
            vm.AllowDefaults = true;
            Assert.That(skin.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(skin.IsExpanded, Is.False);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
