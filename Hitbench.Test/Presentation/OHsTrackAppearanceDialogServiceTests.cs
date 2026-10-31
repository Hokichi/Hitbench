using System.Windows.Threading;
using Hitbench.Services.Dialogs;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public class OHsTrackAppearanceDialogServiceTests
{
    [TestCase("Dark", "#FFFFFF")] [TestCase("Light", "#000000")]
    public void DefaultAppearanceAcceptPreservesNull(string theme, string expected)
    {
        new VolumeWheelTests().SetUp();
        System.Windows.Application.Current.Resources.MergedDictionaries[0] = new System.Windows.ResourceDictionary
        { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        var service = new OHsTrackAppearanceDialogService(() => null, Dispatcher.CurrentDispatcher,
            (request, color) => { Assert.That(color, Is.EqualTo(expected)); return new(request, color); },
            (_, _) => Task.FromResult(true));
        var task = service.RequestAsync(new(null, 7), default);
        UiTestPump.Run(task);
        Assert.That(task.GetAwaiter().GetResult()!.Color, Is.Null);
    }

    [Test] public void CancellationAfterPresentationCannotAccept()
    {
        new VolumeWheelTests().SetUp(); using var cancellation=new CancellationTokenSource();
        var service=new OHsTrackAppearanceDialogService(()=>null,Dispatcher.CurrentDispatcher,(r,c)=>new(r,c),
            (_,_)=>{ cancellation.Cancel(); return Task.FromResult(true); });
        var task=service.RequestAsync(new("#123456", 0),cancellation.Token);
        Assert.Throws<OperationCanceledException>(()=>UiTestPump.Run(task));
    }
}
