using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Onset.Resources.CustomControls;
using Onset.Resources.DataModels;
using Onset.Resources.Messages;
using Onset.Views.Popups;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsControlPointEditorTests
{
    private static OHsControlPointEditor Create(int acceptedVolume, List<SetControlPointRequestedMessage> edits)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var editor = new OHsControlPointEditor { Context = new("project.json", Guid.NewGuid()),
            SetPointCommand = new RelayCommand<SetControlPointRequestedMessage>(request => edits.Add(request!)),
            Point = new(Guid.NewGuid(), 1000, acceptedVolume, "Normal") };
        UiTestPump.Layout(editor, 320, 260);
        return editor;
    }
    private static TextBox Text(OHsControlPointEditor editor) => (TextBox)editor.VolumeControl.Template.FindName("PART_TextBox", editor.VolumeControl);

    [TestCase(0)][TestCase(1)][TestCase(4)][TestCase(5)][TestCase(100)]
    public void InitializationCoercionUntouchedFocusAndRebindingNeverSubmit(int volume)
    {
        var edits = new List<SetControlPointRequestedMessage>(); var editor = Create(volume, edits);
        Assert.That(editor.CommitVolumeInput(false), Is.True);
        Text(editor).RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, Text(editor), null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        editor.VolumeControl.CoerceValue(NumericUpDown.ValueProperty);
        editor.Point = editor.Point! with { Id = Guid.NewGuid() }; UiTestPump.Layout(editor, 320, 260);
        Assert.That(edits, Is.Empty); Assert.That(editor.Point.Volume, Is.EqualTo(volume));
        Assert.That(editor.AcceptedVolumeText, Does.Contain(volume.ToString()));
        UiControlContract.AssertStyle<NumericUpDown>(editor.VolumeControl, "OHsControlPointEditorVolumeStyle");
        UiControlContract.AssertStyle<ComboBox>(editor.SamplesetControl, "OHsControlPointEditorSamplesetStyle");
    }

    [TestCase("5", 5)][TestCase("100", 100)]
    public void ExplicitCommitIncludesFiveEvenWhenCoercedValueAlreadyEqualsFive(string text, decimal expected)
    {
        var edits = new List<SetControlPointRequestedMessage>(); var editor = Create(4, edits);
        Text(editor).Text = text;
        Assert.That(editor.CommitVolumeInput(true), Is.True);
        Assert.That(edits.Single().Volume, Is.EqualTo(expected)); Assert.That(edits[0].Sampleset, Is.Null);
    }

    [TestCase("")][TestCase("bad")][TestCase("50.5")][TestCase("4")][TestCase("101")]
    public void InvalidInputExplainsCorrectionAndDoesNotSend(string text)
    {
        var edits = new List<SetControlPointRequestedMessage>(); var editor = Create(50, edits);
        Text(editor).Text = text;
        Assert.That(editor.CommitVolumeInput(true), Is.False);
        Assert.That(editor.VolumeControl.InputError, Is.Not.Empty); Assert.That(edits, Is.Empty);
        Assert.That(editor.Point!.Volume, Is.EqualTo(50));
    }

    [Test]
    public void SamplesetOnlyEditPreservesLegacyVolumeAndSwitchingPointDropsInvalidDraft()
    {
        var edits = new List<SetControlPointRequestedMessage>(); var editor = Create(4, edits);
        editor.SamplesetControl.SelectedItem = "Drum";
        Assert.That(edits.Single().Sampleset, Is.EqualTo("Drum")); Assert.That(edits[0].Volume, Is.Null);
        Text(editor).Text = "50.5"; editor.CommitVolumeInput(true);
        var next = new ControlPointSnapshot(Guid.NewGuid(), 2000, 75, "Soft"); editor.Point = next;
        Assert.That(editor.VolumeControl.InputError, Is.Null); Assert.That(Text(editor).Text, Is.EqualTo("75"));
        Assert.That(edits, Has.Count.EqualTo(1));
    }

    [TestCase("en-US", "5,5")][TestCase("de-DE", "5.5")]
    public void CultureAmbiguousFractionCannotBecomeDifferentInteger(string culture, string text)
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try { System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var edits = new List<SetControlPointRequestedMessage>(); var editor = Create(50, edits); Text(editor).Text = text;
            Assert.That(editor.CommitVolumeInput(true), Is.False); Assert.That(edits, Is.Empty); }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Test]
    public void LanePopupUsesSubstituteAndClosingNeverDeletesAcceptedPoint()
    {
        var point = new ControlPointSnapshot(Guid.NewGuid(), 0, 4, "Drum");
        var lane = OHsControlPointLaneTests.Create([point]); var shown = new List<bool>();
        lane.PresentEditor = (open, _) => shown.Add(open);
        Assert.That(lane.OpenEditor(point.Id), Is.True); Assert.That(lane.IsEditorOpen, Is.True);
        lane.DismissEditor(); Assert.That(lane.IsEditorOpen, Is.False); Assert.That(shown, Is.EqualTo(new[] { true, false }));
        Assert.That(lane.State!.Points.Single(), Is.EqualTo(point));
        Assert.That(((System.Windows.Controls.Primitives.Popup)lane.FindName("EditorPopup")).IsOpen, Is.False);
    }

    [Test]
    public void ClosingInvalidDraftAndReopeningSameAcceptedPointResetsInputOnly()
    {
        var point = new ControlPointSnapshot(Guid.NewGuid(), 0, 4, "Normal"); var lane = OHsControlPointLaneTests.Create([point]);
        lane.PresentEditor = (_, _) => { }; lane.OpenEditor(point.Id);
        var editor = (OHsControlPointEditor)lane.FindName("ValueEditor"); UiTestPump.Layout(editor, 320, 260);
        Text(editor).Text = "bad"; Assert.That(editor.CommitVolumeInput(true), Is.False);
        lane.DismissEditor(); lane.OpenEditor(point.Id); UiTestPump.Layout(editor, 320, 260);
        Assert.That(editor.VolumeControl.InputError, Is.Null); Assert.That(Text(editor).Text, Is.EqualTo("5"));
        Assert.That(editor.Point!.Volume, Is.EqualTo(4)); lane.DismissEditor();
    }
}
