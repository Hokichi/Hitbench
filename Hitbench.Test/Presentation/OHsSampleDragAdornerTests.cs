using System.Windows;
using Hitbench.Resources.CustomControls;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleDragAdornerTests
{
 [Test]
 public void PlacementUsesExplicitInputsAndHidesOutsideHost()
 {
  var viewport = new Rect(0,0,200,100);
  Assert.That(OHsSampleDragAdorner.PlaceTag(new(40,30),viewport,new(60,20),8,FlowDirection.LeftToRight),Is.EqualTo(new Rect(48,38,60,20)));
  Assert.That(OHsSampleDragAdorner.PlaceTag(new(190,90),viewport,new(60,20),8,FlowDirection.LeftToRight),Is.EqualTo(new Rect(140,62,60,20)));
  Assert.That(OHsSampleDragAdorner.PlaceTag(new(201,30),viewport,new(60,20),8,FlowDirection.LeftToRight),Is.EqualTo(Rect.Empty));
 }
}
