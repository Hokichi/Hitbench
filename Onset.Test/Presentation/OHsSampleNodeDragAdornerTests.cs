using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class OHsSampleNodeDragAdornerTests
{
    [Test]
    public void CustomColorsFollowEachSourceNode()
    {
        var adorner=new OHsSampleNodeDragAdorner(new Border()) {NodeBounds=[new Rect(0,0,10,8),new Rect(20,0,10,8)],TrackColors=["#123456","#ABCDEF"]};
        adorner.Resources["Brush.Text.Primary"]=Brushes.Black;adorner.Resources["Brush.Node.SelectedOutline"]=Brushes.Black;
        UiTestPump.Layout(adorner,200,100);
        var fills=Shapes(VisualTreeHelper.GetDrawing(adorner)!).Where(s=>s.Brush is SolidColorBrush).Select(s=>((SolidColorBrush)s.Brush!).Color).ToArray();
        Assert.That(fills,Is.EqualTo(new[]{Color.FromRgb(0x12,0x34,0x56),Color.FromRgb(0xAB,0xCD,0xEF)}));
    }
    [Test]
    public void DrawsEverySuppliedRectangle()
    {
        var adorner = new OHsSampleNodeDragAdorner(new Border())
        {
            NodeBounds = [new Rect(-5, -4, 10, 8), new Rect(15, 36, 10, 8)],
            CursorPosition = new Point(50, 60)
        };
        adorner.Resources["Brush.Navy"] = Brushes.Blue;
        adorner.Resources["Brush.Border.Default"] = Brushes.White;
        adorner.Resources["Brush.Text.Primary"] = Brushes.White;
        UiTestPump.Layout(adorner, 200, 200);
        var drawings = Shapes(VisualTreeHelper.GetDrawing(adorner)!).Select(d => d.Geometry.Bounds).Distinct().ToArray();
        Assert.That(drawings, Is.EquivalentTo(new[] { new Rect(45, 56, 10, 8), new Rect(65, 96, 10, 8) }));
        Assert.That(adorner.IsHitTestVisible, Is.False);
        adorner.NodeBounds = [new Rect(-5, -4, 10, 8)];
        adorner.CursorPosition = new Point(70, 80); UiTestPump.Layout(adorner, 200, 200);
        Assert.That(Shapes(VisualTreeHelper.GetDrawing(adorner)!).Select(d => d.Geometry.Bounds).Distinct(),
            Is.EqualTo(new[] { new Rect(65, 76, 10, 8) }));
    }

    [Test]
    public void EmptyGroupDrawsNoNodes()
    {
        var adorner = new OHsSampleNodeDragAdorner(new Border());
        UiTestPump.Layout(adorner, 200, 200);
        var drawing = VisualTreeHelper.GetDrawing(adorner);
        Assert.That(drawing is null ? [] : Shapes(drawing).ToArray(), Is.Empty);
        Assert.That(adorner.IsHitTestVisible, Is.False);
    }

    private static IEnumerable<GeometryDrawing> Shapes(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry) yield return geometry;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var shape in Shapes(child)) yield return shape;
    }
}
