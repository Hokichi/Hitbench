using Onset.Resources.DataModels;
using Onset.ViewModels.Popups;

namespace Onset.Test.Presentation;

public sealed class MappingConflictsPopupVMTests
{
    [Test] public void IncomingIsDefaultAndExactlyOneChoiceSurvives()
    {
        var vm = new MappingConflictsPopupVM([new("C3", "old/C3.wav", new[] { "new/C3.wav" })]);
        Assert.That(vm.Confirm()["C3"], Is.EqualTo("new/C3.wav"));
        var row = vm.Rows.Single(); var existing = row.Choices.Single(c => c.IsExisting);
        existing.IsChecked = true;
        Assert.That(vm.Confirm()["C3"], Is.EqualTo("old/C3.wav"));
        existing.IsChecked = false;
        Assert.That(existing.IsChecked, Is.True); Assert.That(row.Choices.Count(c => c.IsChecked), Is.EqualTo(1));
    }
    [Test] public void DuplicateIncomingFilesStayVisibleWithStableDefaultAndImmutableResult()
    {
        var input = new List<string> { "new/C#4.wav", "new/C#4.mp3", "new/C#4.ogg" };
        var conflict = new MappingConflict("C#4", null, input); input.Clear();
        var vm = new MappingConflictsPopupVM([conflict, new("C3", "old/C3.wav", new[] { "new/C3.wav" }, true)]);
        Assert.That(vm.Rows.Select(r => r.Key), Is.EqualTo(new[] { "C3", "C#4" }));
        Assert.That(vm.Rows.Last().Choices, Has.Count.EqualTo(3));
        Assert.That(vm.Confirm()["C#4"], Is.EqualTo("new/C#4.mp3"));
        var result = vm.Confirm(); vm.Rows.Last().Choices.Last().IsChecked = true;
        Assert.That(result["C#4"], Is.EqualTo("new/C#4.mp3"));
        Assert.That(vm.Rows.First().Choices.Single(c => c.IsExisting).IsMissing, Is.True);
    }
}
