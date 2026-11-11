using System.Globalization;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

public class OHsMidiImportPopupVMTests
{
    [Test]
    public void TimingChoiceRetainsTypedSelection()
    {
        var vm = new OHsMidiImportPopupVM(Services.OHsMidiImportMapperTests.Source(), [new() { Offset = 0, BPM = 180 }]);
        Assert.That(vm.SelectedMode, Is.EqualTo(OHsMidiImportMode.AsIs));
        vm.SelectedMode = OHsMidiImportMode.ProjectBpm;
        Assert.That(vm.SelectedMode, Is.EqualTo(OHsMidiImportMode.ProjectBpm));
        Assert.That(vm.CanImport, Is.True);
        vm.SelectedMode = (OHsMidiImportMode)99;
        Assert.That(vm.CanImport, Is.False);
    }

    [Test]
    public void FullTimingDataRemainsAvailableForVariableMaps()
    {
        var vm = new OHsMidiImportPopupVM(Services.OHsMidiImportMapperTests.Source(), [new() { Offset = -100, BPM = 180 }, new() { Offset = 1000, BPM = 240 }]);
        Assert.That(vm.MidiBpmText, Does.Contain(120.ToString(CultureInfo.CurrentCulture)));
        Assert.That(vm.ProjectBpmText, Does.Contain(180.ToString(CultureInfo.CurrentCulture)));
        Assert.That(vm.FullTimingText, Does.Contain("240").And.Contain("1000").And.Contain("-100"));
    }

    [Test]
    public void TimingSummaryUsesFirstEffectiveProjectPoint()
    {
        var vm = new OHsMidiImportPopupVM(Services.OHsMidiImportMapperTests.Source(),
            [new() { Offset = 500, BPM = 240 }, new() { Offset = 0, BPM = 100 }, new() { Offset = 0, BPM = 180 }]);
        Assert.That(vm.ProjectBpmText, Does.StartWith("180 BPM"));
        Assert.That(vm.FullTimingText, Does.Contain("500 ms: 240 BPM"));
        Assert.That(vm.FullTimingText, Does.Not.Contain("0 ms: 100 BPM"));
    }
}
