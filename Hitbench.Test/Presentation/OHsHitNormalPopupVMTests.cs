using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

public class OHsHitNormalPopupVMTests
{
    [Test]
    public void DraftPreservesMissingPathsAndIndependentSelections()
    {
        var original = new OHsHitNormalConfiguration("gone.wav", "", "d.wav");
        var vm = new OHsHitNormalPopupVM(original, [new("d.wav", "Samples/d.wav")]);
        Assert.That(vm.Choices[0].Path, Is.Empty);
        Assert.That(vm.Choices[0].DisplayName, Is.EqualTo("Default"));
        Assert.That(vm.Choices.Select(c => c.Path), Does.Contain("gone.wav"));
        vm.SoftPath = "d.wav"; vm.DrumPath = "";
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsHitNormalConfiguration("gone.wav", "d.wav", "")));
        Assert.That(original, Is.EqualTo(new OHsHitNormalConfiguration("gone.wav", "", "d.wav")));
    }

    [Test]
    public void InvalidDraftCannotCommit()
    {
        var vm = new OHsHitNormalPopupVM(new(), []);
        Assert.That(vm.CanSave, Is.True);
        vm.NormalPath = "../bad.wav";
        Assert.That(vm.CanSave, Is.False);
        Assert.Throws<InvalidOperationException>(() => vm.CreateResult());
    }

    [Test]
    public void ReopeningMatchesWindowsPathsIgnoringCase()
    {
        var vm = new OHsHitNormalPopupVM(new("A.wav"), [new("a.wav", "Samples/a.wav")]);
        Assert.That(vm.NormalPath, Is.EqualTo(vm.Choices.Single(c => c.Path.Length > 0).Path));
    }
}
