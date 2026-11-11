using System.Windows.Threading;
using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsMidiImportDialogServiceTests
{
    [Test]
    public void SubstitutedBoundariesNeverNeedNativeOwnerOrWindows()
    {
        var messages = new List<string>();
        var service = new OHsMidiImportDialogService(() => throw new InvalidOperationException("Native owner must not be read"), Dispatcher.CurrentDispatcher,
            (s, p) => new(s, p), picker: _ => Task.FromResult<string?>("selected.mid"),
            present: (vm, _) => { vm.SelectedMode = OHsMidiImportMode.ProjectBpm; return Task.FromResult(true); },
            message: (title, text, confirm, _) => { messages.Add(text); return Task.FromResult(!confirm); });
        Assert.That(Complete(service.PickFileAsync(default)), Is.EqualTo("selected.mid"));
        Assert.That(Complete(service.ChooseTimingAsync(Services.OHsMidiImportMapperTests.Source(), [new() { Offset = 0, BPM = 180 }], default)), Is.EqualTo(OHsMidiImportMode.ProjectBpm));
        Assert.That(Complete(service.ConfirmExclusionsAsync(Services.OHsMidiImportMapperTests.Source(), default)), Is.False);
        UiTestPump.Run(service.ShowErrorAsync("Cannot import MIDI", "broken", default));
        Assert.That(messages.Last(), Is.EqualTo("broken"));
    }

    [Test]
    public void CancellationAfterPresenterCannotAcceptMode()
    {
        using var canceled = new CancellationTokenSource();
        var service = new OHsMidiImportDialogService(() => null, Dispatcher.CurrentDispatcher, (s, p) => new(s, p),
            present: (_, _) => { canceled.Cancel(); return Task.FromResult(true); });
        Assert.That(() => UiTestPump.Run(service.ChooseTimingAsync(Services.OHsMidiImportMapperTests.Source(), [new() { Offset = 0, BPM = 180 }], canceled.Token)), Throws.InstanceOf<OperationCanceledException>());
    }

    private static T Complete<T>(Task<T> task) { UiTestPump.Run(task); return task.GetAwaiter().GetResult(); }
}
