using System.Windows.Controls;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class NumericUpDownIntegerTests
{
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
