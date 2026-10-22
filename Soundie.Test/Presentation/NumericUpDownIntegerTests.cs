using System.Windows.Controls;
using Soundie.Resources.CustomControls;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class NumericUpDownIntegerTests
{
    [Test]
    public void RecycledEntryDoesNotInheritAnotherPointsInvalidDraft()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var first=new StubNumericValue { Volume = 50 };
        var second=new StubNumericValue { Volume = 50 };
        var control=new NumericUpDown { Style=(System.Windows.Style)System.Windows.Application.Current.FindResource(typeof(NumericUpDown)),IsIntegerOnly=true,LowerLimit=0,UpperLimit=100,DataContext=first };
        System.Windows.Data.BindingOperations.SetBinding(control,NumericUpDown.ValueProperty,new System.Windows.Data.Binding("Volume") {Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged});
        UiTestPump.Layout(control,180,40); var editor=(TextBox)control.Template.FindName("PART_TextBox",control);
        editor.Text="50.5"; Assert.That(control.CommitInput(),Is.False);
        control.DataContext=second; control.GetBindingExpression(NumericUpDown.ValueProperty)!.UpdateTarget();
        Assert.That(control.InputError,Is.Null); Assert.That(editor.Text,Is.EqualTo("50"));
        Assert.That(first.Volume,Is.EqualTo(50)); Assert.That(second.Volume,Is.EqualTo(50));
    }
    [TestCase("en-US", "5,5")][TestCase("de-DE", "5.5")]
    public void GroupSeparatorCannotTurnFractionalDraftIntoDifferentInteger(string culture, string text)
    {
        var previous=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo(culture);
            new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
            var control=new NumericUpDown { Style=(System.Windows.Style)System.Windows.Application.Current.FindResource(typeof(NumericUpDown)),IsIntegerOnly=true,LowerLimit=0,UpperLimit=100,Value=50 };
            UiTestPump.Layout(control,180,40); ((TextBox)control.Template.FindName("PART_TextBox",control)).Text=text;
            Assert.That(control.CommitInput(),Is.False); Assert.That(control.Value,Is.EqualTo(50));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture=previous; }
    }
    [TestCase("50.5")][TestCase("")][TestCase("bad")][TestCase("101")][TestCase("-1")]
    public void InvalidIntegerDraftPreservesValueAndExplainsCorrection(string draft)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var control=new NumericUpDown { Style=(System.Windows.Style)System.Windows.Application.Current.FindResource(typeof(NumericUpDown)), IsIntegerOnly=true,LowerLimit=0,UpperLimit=100,Value=50 };
        UiTestPump.Layout(control,180,40);
        var editor=(TextBox)control.Template.FindName("PART_TextBox",control);
        editor.Text=draft; Assert.That(control.CommitInput(),Is.False);
        Assert.That(control.Value,Is.EqualTo(50)); Assert.That(control.InputError,Does.Contain("0").And.Contain("100"));
        editor.Text="25"; Assert.That(control.CommitInput(),Is.True);
        Assert.That(control.Value,Is.EqualTo(25)); Assert.That(control.InputError,Is.Null);
    }
    [Test]
    public void DecimalContractRemainsAvailable()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var control=new NumericUpDown { Style=(System.Windows.Style)System.Windows.Application.Current.FindResource(typeof(NumericUpDown)), LowerLimit=0,UpperLimit=100 }; UiTestPump.Layout(control,180,40);
        var editor=(TextBox)control.Template.FindName("PART_TextBox",control); editor.Text="50.5";
        Assert.That(control.CommitInput(),Is.True); Assert.That(control.Value,Is.EqualTo(50.5m));
    }
}
