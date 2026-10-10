using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public class NodePastePopupVMTests
{
    [Test]
    public void MarkerDefaultAndRadioChoiceAreExclusive()
    {
        var vm = new NodePastePopupVM();
        Assert.That(vm.UsePlaybackMarker, Is.True);
        vm.UseSourceTimestamps = true;
        Assert.That(vm.Timing, Is.EqualTo(NodePasteTiming.SourceTimestamps));
        Assert.That(vm.UsePlaybackMarker, Is.False);
        vm.UsePlaybackMarker = true;
        Assert.That(vm.UseSourceTimestamps, Is.False);
    }

}
