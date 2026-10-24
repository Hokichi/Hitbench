using System.Windows.Threading;
using Onset.Services.Dialogs;
namespace Onset.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public class OHsTrackAppearanceDialogServiceTests
{
    [Test] public void CancellationAfterPresentationCannotAccept()
    {
        new VolumeWheelTests().SetUp(); using var cancellation=new CancellationTokenSource();
        var service=new OHsTrackAppearanceDialogService(()=>null,Dispatcher.CurrentDispatcher,(r,c)=>new(r,c),
            (_,_)=>{ cancellation.Cancel(); return Task.FromResult(true); });
        var task=service.RequestAsync(new("Old","#123456",0),cancellation.Token);
        Assert.Throws<OperationCanceledException>(()=>UiTestPump.Run(task));
    }
}
