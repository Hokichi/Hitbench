using Onset.Resources.DataModels;
using Onset.ViewModels.Popups;
namespace Onset.Test.Presentation;
public class OHsTrackAppearancePopupVMTests
{
    [Test] public void NameOnlySavePreservesAutomaticColor()
    {
        var request=new OHsTrackAppearanceRequest("Old",null,0);
        var vm=new OHsTrackAppearancePopupVM(request,"#123456");
        Assert.That(vm.IsDirty, Is.False); vm.Name="";
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsTrackAppearanceResult("",null)));
        Assert.That(request.Name, Is.EqualTo("Old"));
        vm.Color="#abcdef"; Assert.That(vm.CreateResult().Color, Is.EqualTo("#ABCDEF"));
        vm.Color="#123456"; vm.Name="Old"; Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.CreateResult().Color, Is.Null);
    }
    [Test] public void InvalidColorCannotSave()
    {
        var vm=new OHsTrackAppearancePopupVM(new("Old","#123456",0),"#123456"){ Color="bad" };
        Assert.That(vm.CanSave, Is.False); Assert.Throws<InvalidOperationException>(()=>vm.CreateResult());
    }
}
