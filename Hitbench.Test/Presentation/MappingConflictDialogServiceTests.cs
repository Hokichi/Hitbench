using System.Windows.Threading;
using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class MappingConflictDialogServiceTests
{
    [TestCase(false)][TestCase(true)]
    public void DialogReturnsImmutableChoicesOrCancellation(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var service = new MappingConflictDialogService(() => null, Dispatcher.CurrentDispatcher, (vm, _) =>
        {
            if (cancel) cancellation.Cancel();
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(vm.Confirm());
        });
        var selecting = service.SelectAsync([new MappingConflict("C3", "old", new[] { "new" })], cancellation.Token);
        UiTestPump.Run(selecting);
        var result = selecting.GetAwaiter().GetResult();
        if (cancel) Assert.That(result, Is.Null); else Assert.That(result!["C3"], Is.EqualTo("new"));
    }
}
