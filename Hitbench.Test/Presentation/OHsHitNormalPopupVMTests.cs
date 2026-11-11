using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

public class OHsHitNormalPopupVMTests
{
    private static OHsHitNormalPopupVM CreateCatalog(OHsHitNormalConfiguration? current = null) => new(current ?? new(),
        [new("SkinDefault/normal-hitnormal.wav", "normal-hitnormal.wav", OHsHitNormalSampleSource.Skin),
         new("SkinDefault/soft-hitnormal.ogg", "soft-hitnormal.ogg", OHsHitNormalSampleSource.Skin),
         new("SkinDefault/drum-hitnormal.wav", "drum-hitnormal.wav", OHsHitNormalSampleSource.Skin),
         new("a.wav", "Samples/a.wav")]);

    [Test]
    public void MatchingDefaultsAreCurrentAndPinnedOnce()
    {
        var vm = CreateCatalog();
        Assert.That(vm.SelectedSlot.Name, Is.EqualTo("Normal"));
        Assert.That(vm.Slots.Select(s => s.Sample.DisplayFileName),
            Is.EqualTo(new[] { "normal-hitnormal.wav", "soft-hitnormal.ogg", "drum-hitnormal.wav" }));
        Assert.That(vm.PinnedSamples, Has.Count.EqualTo(1));
        Assert.That(vm.PinnedSamples[0].IsDefault && vm.PinnedSamples[0].IsCurrent, Is.True);
        Assert.That(vm.CatalogSamples.Count(s => s.Path == vm.PinnedSamples[0].Path), Is.EqualTo(1));
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsHitNormalConfiguration()));
    }

    [Test]
    public void AssignmentChangesOnlySelectedSlotAndSwitchingOnlyRebuildsPins()
    {
        var vm = CreateCatalog();
        var custom = vm.CatalogSamples.Single(s => s.Path == "a.wav");
        vm.AssignSampleCommand.Execute(custom);
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsHitNormalConfiguration("a.wav")));
        Assert.That(vm.PinnedSamples.Select(s => s.Path), Is.EqualTo(new[] { "SkinDefault/normal-hitnormal.wav", "a.wav" }));
        Assert.That(custom.Group.IsPinned && custom.IsCurrent, Is.True);
        vm.Groups[1].IsExpanded = false;
        vm.SelectedSlot = vm.Slots[1];
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsHitNormalConfiguration("a.wav")));
        Assert.That(vm.PinnedSamples.Single().Path, Is.EqualTo("SkinDefault/soft-hitnormal.ogg"));
        Assert.That(custom.Group.Name, Is.EqualTo("Project Samples"));
        Assert.That(custom.IsCurrent, Is.False);
        Assert.That(vm.Groups[1].IsExpanded, Is.False);
        vm.AssignSampleCommand.Execute(vm.CatalogSamples.Single(s => s.Path == "SkinDefault/drum-hitnormal.wav"));
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsHitNormalConfiguration("a.wav", "SkinDefault/drum-hitnormal.wav")));
        vm.AssignSampleCommand.Execute(vm.PinnedSamples[0]);
        Assert.That(vm.SoftPath, Is.Empty);
    }

    [Test]
    public void DefaultIdentityIgnoresPathCaseAndPreservesSavedAssignmentUntilChanged()
    {
        var vm = CreateCatalog(new("SKINDEFAULT/NORMAL-HITNORMAL.WAV"));
        Assert.That(vm.PinnedSamples, Has.Count.EqualTo(1));
        Assert.That(vm.PinnedSamples[0].IsDefault && vm.PinnedSamples[0].IsCurrent, Is.True);
        vm.AssignSampleCommand.Execute(vm.PinnedSamples[0]);
        Assert.That(vm.NormalPath, Is.Empty);
    }

    [Test]
    public void MissingDefaultsAndSavedPathsRemainReplaceableAndForeignEntriesCannotAssign()
    {
        var vm = new OHsHitNormalPopupVM(new("gone.wav"), []);
        Assert.That(vm.PinnedSamples.Select(s => s.DisplayFileName), Is.EqualTo(new[] { "normal-hitnormal", "gone.wav" }));
        Assert.That(vm.PinnedSamples.All(s => s.Waveform is null), Is.True);
        var foreign = new OHsHitNormalSample("bad.wav", "bad.wav", OHsHitNormalSampleSource.Project, vm.Groups[3]);
        Assert.That(vm.AssignSampleCommand.CanExecute(foreign), Is.False);
        vm.AssignSampleCommand.Execute(foreign);
        Assert.That(vm.NormalPath, Is.EqualTo("gone.wav"));
        vm.AssignSampleCommand.Execute(vm.PinnedSamples[0]);
        Assert.That(vm.NormalPath, Is.Empty);
    }

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
