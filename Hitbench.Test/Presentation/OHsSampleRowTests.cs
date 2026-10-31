using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleRowTests
{
    [Test]
    public void MissingActionsTemplateDoesNotRenderEntryFallback()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var entry = new object();
        var row = new OHsSampleRow { DataContext = entry, FileName = "hit.wav" };
        UiTestPump.Layout(row, 500, 120);

        Assert.That(Children(row).OfType<BalloonButton>().Count(), Is.EqualTo(1));
        Assert.That(Children(row).OfType<TextBlock>().Select(text => text.Text), Does.Not.Contain(entry.ToString()));
    }

    [Test]
    public void ChangingActionsTemplateRebindsAndRemovesActions()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var resources = (ResourceDictionary)XamlReader.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:c="clr-namespace:Hitbench.Resources.CustomControls;assembly=Hitbench">
                <DataTemplate x:Key="TestSampleActions">
                    <c:BalloonButton ButtonText="Test action" ButtonIcon="{StaticResource Check}"
                        Style="{StaticResource OHsBalloonNeutralActionStyle}"
                        Command="{Binding PlayCommand, RelativeSource={RelativeSource AncestorType=c:OHsSampleRow}, Mode=OneWay}"
                        CommandParameter="{Binding Mode=OneWay}" />
                </DataTemplate>
            </ResourceDictionary>
            """);
        var template = (DataTemplate)resources["TestSampleActions"];
        var first = new object();
        var second = new object();
        object? invoked = null;
        var row = new OHsSampleRow
        {
            DataContext = first, FileName = "hit.wav", ActionsTemplate = template,
            PlayCommand = new RelayCommand<object>(value => invoked = value)
        };
        UiTestPump.Layout(row, 500, 120);
        var action = Children(row).OfType<BalloonButton>().Single(button => button.ButtonText == "Test action");
        action.Command.Execute(action.CommandParameter);
        Assert.That(invoked, Is.SameAs(first));

        row.ActionsTemplate = null;
        row.DataContext = second;
        UiTestPump.Layout(row, 500, 120);
        Assert.That(Children(row).OfType<BalloonButton>().Any(button => button.ButtonText == "Test action"), Is.False);
        Assert.That(Children(row).OfType<TextBlock>().Select(text => text.Text), Does.Not.Contain(second.ToString()));

        row.ActionsTemplate = template;
        UiTestPump.Layout(row, 500, 120);
        action = Children(row).OfType<BalloonButton>().Single(button => button.ButtonText == "Test action");
        action.Command.Execute(action.CommandParameter);
        Assert.That(invoked, Is.SameAs(second));
        Assert.That(row.DataContext, Is.SameAs(second));
    }

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

    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
