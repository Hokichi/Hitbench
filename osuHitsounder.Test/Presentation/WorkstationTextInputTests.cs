using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationTextInputTests
{
    [SetUp]
    public void Setup() => new VolumeWheelTests().SetUp();

    [TestCase("text")]
    [TestCase("password")]
    [TestCase("visual")]
    [TestCase("content")]
    public void SpaceControlsPlaybackFromTextDescendants(string kind)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        var text = new TextBox { Style = (Style)Application.Current.FindResource("RoundedTextInputStyle") };
        UiTestPump.Layout(text, 200, 40);
        var run = new Run("Track name");
        var rich = new RichTextBox(new FlowDocument(new Paragraph(run)));
        DependencyObject origin = kind switch
        {
            "password" => new PasswordBox(),
            "visual" => VisualTreeHelper.GetChild(text, 0),
            "content" => run,
            _ => text
        };
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, origin), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, true, origin), Is.True);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.Shift, false, origin), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(vm.IsPlaying, Is.False);
        Assert.That(run.Text, Is.EqualTo("Track name"));
        GC.KeepAlive(rich);
    }
}
