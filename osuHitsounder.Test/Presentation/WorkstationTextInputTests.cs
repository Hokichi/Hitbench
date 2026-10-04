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
    public void TextOriginSpaceIsUnhandledAndPreservesPlayback(string kind)
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
        foreach (var playing in new[] { false, true })
        {
            if (playing) UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
            var position = vm.Position;
            foreach (var modifier in new[] { ModifierKeys.None, ModifierKeys.Shift })
            foreach (var repeat in new[] { false, true })
            {
                Assert.That(page.HandlePlaybackKey(Key.Space, modifier, repeat, origin), Is.False);
                Assert.That(vm.IsPlaying, Is.EqualTo(playing));
                Assert.That(vm.Position, Is.EqualTo(position));
            }
        }
        GC.KeepAlive(rich);
    }
}
