using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Onset.Services;
using Onset.ViewModels.Shell;
using Onset.Views.Pages;

namespace Onset.Test.Presentation;

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
    [TestCase("editable")]
    public void DivisorShortcutPreservesTextInput(string kind)
    {
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        var text = new TextBox { Text = "Track", Style = (Style)Application.Current.FindResource("RoundedTextInputStyle") };
        UiTestPump.Layout(text, 200, 40);
        var run = new Run("Track");
        var rich = new RichTextBox(new FlowDocument(new Paragraph(run)));
        DependencyObject origin = kind switch
        {
            "password" => new PasswordBox(), "visual" => VisualTreeHelper.GetChild(text, 0),
            "content" => run, "editable" => new ComboBox { IsEditable = true }, _ => text
        };
        Assert.That(page.HandlePlaybackKey(Key.D3, ModifierKeys.Control, false, origin), Is.False);
        Assert.That(vm.Subdivision, Is.EqualTo(4));
        Assert.That(text.Text, Is.EqualTo("Track"));
        Assert.That(run.Text, Is.EqualTo("Track"));
        UiTestPump.Run(page.DeactivateAsync());
        GC.KeepAlive(rich);
    }

    [TestCase("text")]
    [TestCase("password")]
    [TestCase("visual")]
    [TestCase("content")]
    [TestCase("editable")]
    public void SpacePreservesPlaybackFromTextDescendants(string kind)
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
            "editable" => new ComboBox { IsEditable = true },
            _ => text
        };
        UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, origin), Is.False);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, true, origin), Is.False);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.Shift, false, origin), Is.False);
        Assert.That(vm.IsPlaying, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.D1, ModifierKeys.None, false, origin), Is.False);
        Assert.That(page.HandlePlaybackKey(Key.D2, ModifierKeys.None, false, origin), Is.False);
        Assert.That(run.Text, Is.EqualTo("Track name"));
        GC.KeepAlive(rich);
        UiTestPump.Run(page.DeactivateAsync());
    }
}
