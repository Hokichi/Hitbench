using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Views.Popups;
using CommunityToolkit.Mvvm.Input;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class NumericUpDownEnterTests
{
    [Test]
    public void InvalidDecimalEnterDoesNotNotifyOwner()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var numeric = new NumericUpDown { Style = (Style)Application.Current.FindResource(typeof(NumericUpDown)), Value = 10 };
        UiTestPump.Layout(numeric, 200, 40);
        ((TextBox)numeric.Template.FindName("PART_TextBox", numeric)).Text = "bad";
        var commits = 0; numeric.InputCommitted += (_, _) => commits++;
        using var source = new InMemoryPresentationSource();
        numeric.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.That(commits, Is.Zero); Assert.That(numeric.Value, Is.EqualTo(10));
    }
    [TestCase("75", 1)]
    [TestCase("bad", 0)]
    [TestCase("101", 0)]
    public void EnterSavesControlPointOnceWithoutFocusLoss(string draft, int count)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var edits = new List<SetControlPointRequestedMessage>();
        var editor = new OHsControlPointEditor { Context = new("project.json", Guid.NewGuid()), Point = new(Guid.NewGuid(), 100, 50, "Normal"), SetPointCommand = new RelayCommand<SetControlPointRequestedMessage>(m => edits.Add(m!)) };
        UiTestPump.Layout(editor, 320, 260);
        var numeric = editor.VolumeControl;
        var text = (TextBox)numeric.Template.FindName("PART_TextBox", numeric); text.Text = draft;
        using var source = new InMemoryPresentationSource();
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        text.RaiseEvent(key);
        Assert.That(edits.Count, Is.EqualTo(count)); Assert.That(key.Handled, Is.True);
        if (count > 0) Assert.That(edits[0].Volume, Is.EqualTo(75));
    }
}
