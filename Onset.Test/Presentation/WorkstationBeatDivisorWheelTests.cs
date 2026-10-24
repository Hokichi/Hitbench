using System.Windows;
using System.Windows.Input;
using Onset.ViewModels.Shell;
namespace Onset.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class WorkstationBeatDivisorWheelTests
{
    [Test]
    public void ControlAltAccumulatesPartialDeltasAndAltNoLongerChangesDivisor()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var page=WorkstationTestPage.Create(); page.SetProjectPath("project.json");
        using var view=new InMemoryView(new Window {Content=page,Width=900,Height=600});
        UiTestPump.Run(page.LoadCompletion);
        var vm=(WorkstationPageVM)page.DataContext;vm.SelectedSubdivisionIndex=3;
        Assert.That(page.HandleContextWheel(ModifierKeys.Control|ModifierKeys.Alt,60,page),Is.True);
        Assert.That(vm.SelectedSubdivisionIndex,Is.EqualTo(3));
        Assert.That(page.HandleContextWheel(ModifierKeys.Control|ModifierKeys.Alt,60,page),Is.True);
        Assert.That(vm.SelectedSubdivisionIndex,Is.EqualTo(4));
        page.HandleContextWheel(ModifierKeys.Alt,120,page.Timeline);
        Assert.That(vm.SelectedSubdivisionIndex,Is.EqualTo(4));
        UiTestPump.Run(page.DeactivateAsync());
    }
}
