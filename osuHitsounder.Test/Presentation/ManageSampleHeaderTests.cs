using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Resources.CustomControls;

using System.Windows.Media;
using System.Windows;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class ManageSampleHeaderTests
{
    [Test] public void HeaderOffersManagerAction()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices();
        var shell = services.GetRequiredService<MainWindow>();
        try
        {
            var actions = Descendants((DependencyObject)shell.Content).OfType<BalloonButton>();
            Assert.That(actions.Any(button => button.ButtonText == "Manage Sample"), Is.True);
        }
        finally { shell.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}


