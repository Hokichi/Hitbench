using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ManageSamplesSkinDefaultsIntegrationTests
{
    [Test]
    public void DrawerUsesExpandersAndCanonicalCheckboxWithoutTabStops()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var files = new StubSampleFileService(["a.wav"]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files), new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        try
        {
            var drawer = new ManageSamplesDrawer(vm); KeyboardNavigationPolicy.SetSuppressTabNavigation(drawer, true);
            UiTestPump.Layout(drawer, 400, 600);
            var skin = drawer.FindName("SkinDefaultsExpander") as Expander;
            var project = drawer.FindName("ProjectSamplesExpander") as Expander;
            Assert.That(skin, Is.Not.Null); Assert.That(project, Is.Not.Null);
            UiControlContract.AssertStyle<Expander>(skin!, "OHsManageSamplesSkinExpanderStyle");
            Assert.That(skin!.Style.BasedOn, Is.SameAs(skin.FindResource(typeof(Expander))));
            var checkbox = (CheckBox)drawer.FindName("AllowDefaultsCheckBox");
            UiControlContract.AssertStyle<CheckBox>(checkbox, "RegularCheckBoxStyle");
            Assert.That(checkbox.IsTabStop, Is.False);
            Assert.That(skin!.IsExpanded, Is.True); skin.IsExpanded = false;
            Assert.That(project!.IsExpanded, Is.True);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
