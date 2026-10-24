using System.Windows;
using System.Windows.Media;
using Onset.Core.Entities;
using Onset.Resources.CustomControls;
namespace Onset.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class StaticWaveformTests
{
    [Test]
    public void MarkerCanBeSuppressedWithoutChangingSeek()
    {
        var view=new WaveformView {Data=new WaveformData(TimeSpan.FromSeconds(10),[.2f,.5f])};
        UiTestPump.Layout(view,200,60);view.PrepareDrawing();
        Assert.That(view.ShowPlaybackMarker,Is.True);
        view.ShowPlaybackMarker=false;view.PrepareDrawing();
        Assert.That(((DrawingVisual)VisualTreeHelper.GetChild(view,1)).Drawing,Is.Null);
        Assert.That(view.GetPositionAt(100),Is.EqualTo(TimeSpan.FromSeconds(5)));
        view.ShowPlaybackMarker=true;view.PrepareDrawing();
        Assert.That(((DrawingVisual)VisualTreeHelper.GetChild(view,1)).Drawing,Is.Not.Null);
    }
}
