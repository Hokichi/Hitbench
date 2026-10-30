using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleRowTests
{
    [Test]
    public void RowRetainsDataContextAndInvokesPlayWithItsParameter()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var value = new object(); object? played = null;
        var row = new OHsSampleRow { DataContext = value, FileName = "hit.wav", PlayCommand = new RelayCommand<object>(p => played = p), PlayCommandParameter = value };
        UiTestPump.Layout(row, 500, 120);
        var play = (BalloonButton)row.FindName("PlayAction");
        Assert.That(play.Style.BasedOn, Is.SameAs(row.FindResource("OHsBalloonNeutralActionStyle")));
        play.Command.Execute(play.CommandParameter);
        Assert.That(played, Is.SameAs(value));
        Assert.That(row.DataContext, Is.SameAs(value));
    }
}
