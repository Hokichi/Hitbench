using osuHitsounder.Views.Pages;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationAudioCardTests
{
    [Test]
    public void ElapsedTimeFollowsExistingAuthority()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var audio = new StubWorkstationAudioService
        {
            Analyze = (_, _) => Task.FromResult(new WaveformData(TimeSpan.FromMinutes(70), [.5f]))
        };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default));
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var card = new OHsWorkstationAudioCard { DataContext = vm };
            var footer = new OHsWorkstationFooter { DataContext = vm };
            UiTestPump.Layout(card, 900, 300);
            UiTestPump.Layout(footer, 900, 100);
            UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromMinutes(65.25)));
            var elapsed = card.FindName("ElapsedTimeText") as TextBlock;
            Assert.That(elapsed, Is.Not.Null);
            Assert.That(elapsed!.Text, Is.EqualTo("65:15.000"));
            Assert.That(BindingOperations.GetBinding(elapsed, TextBlock.TextProperty)!.Mode, Is.EqualTo(BindingMode.OneWay));
            var bindings = Descendants(card).Concat(Descendants(footer)).OfType<TextBlock>()
                .Count(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == nameof(vm.ElapsedText));
            Assert.That(bindings, Is.EqualTo(1));
            UiControlContract.AssertStyle<TextBlock>(elapsed, "OHsWorkstationElapsedStyle");
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    {
        yield return owner;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(owner); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(owner, index))) yield return child;
    }

    [Test]
    public void ComponentUsesExistingViewModelContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        var owner = new OHsWorkstationAudioCard { DataContext = vm };
        UiTestPump.Layout(owner, 900, 250);
        var action = (osuHitsounder.Resources.CustomControls.BalloonButton)owner.FindName("AddSampleTrackButton");
        Assert.That(action.Command, Is.SameAs(vm.AddSampleTrackCommand));
        Assert.That(owner.Timeline.DataContext, Is.SameAs(vm));
        UiTestPump.Run(vm.DeactivateAsync());
    }
}
