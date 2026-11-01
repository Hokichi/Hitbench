using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;
public class OHsTrackAppearancePopupVMTests
{
    [TestCase("")][TestCase("Kick:/?*")]
    public void NameOnlyDraftPreservesAutomaticColor(string name)
    {
        var vm = new OHsTrackAppearancePopupVM(new(null, 0) { Name = "Old" }, "#FFFFFF");
        vm.Name = name;
        Assert.That(vm.CanSave, Is.True);
        Assert.That(vm.IsDirty, Is.True);
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsTrackAppearanceResult(null) { Name = name }));
        vm.Name = "Old";
        Assert.That(vm.IsDirty, Is.False);
    }
    [Test] public void UnchangedSavePreservesAutomaticColor()
    {
        var request=new OHsTrackAppearanceRequest(null, 0);
        var vm=new OHsTrackAppearancePopupVM(request,"#123456");
        Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsTrackAppearanceResult(null) { Name = "" }));
        vm.Color="#abcdef"; Assert.That(vm.CreateResult().Color, Is.EqualTo("#ABCDEF"));
        vm.Color="#123456"; Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.CreateResult().Color, Is.Null);
    }
    [Test] public void InvalidColorCannotSave()
    {
        var vm=new OHsTrackAppearancePopupVM(new("#123456", 0),"#123456"){ Color="bad" };
        Assert.That(vm.CanSave, Is.False); Assert.Throws<InvalidOperationException>(()=>vm.CreateResult());
    }
}
