using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA),NonParallelizable]
public sealed class OHsMapsetSampleImportPopupIntegrationTests
{
    [TestCase("Dark",false, false)] [TestCase("Light",true, false)]
    [TestCase("Dark",true, true)] [TestCase("Light",false, true)]
    public void CheckboxBindingAndPopupResourcesPreserveSelection(string theme,bool suppress, bool duplicate)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var app=Application.Current; var original=app.Resources.MergedDictionaries[0];
        var vm=new OHsMapsetSampleImportPopupVM(["drum-hitnormal9.wav"], duplicate ? ["drum-hitnormal9.wav"] : []);
        var popup=new OHsMapsetSampleImportPopup(vm,_=>{});
        try
        {
            app.Resources.MergedDictionaries[0]=new ResourceDictionary { Source=new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml",UriKind.Relative) };
            var root=(FrameworkElement)popup.Content;root.Resources.MergedDictionaries.Add(popup.Resources);
            KeyboardNavigationPolicy.SetSuppressTabNavigation(popup,suppress);
            KeyboardNavigationPolicy.BindSuppressionScope(root,popup);
            using var view=new InMemoryView(popup,scheduleLayout:false);
            var expander=(Expander)popup.FindName("SamplesExpander");
            Assert.That(expander.IsExpanded,Is.False);UiControlContract.AssertStyle<Expander>(expander,typeof(Expander));
            expander.IsExpanded=true;view.UpdateLayout();
            var checkbox=Children(view.Root).OfType<CheckBox>().Single();
            UiControlContract.AssertStyle<CheckBox>(checkbox,"OHsMapsetImportCheckStyle");
            Assert.That(checkbox.Style.BasedOn,Is.SameAs(checkbox.FindResource("RegularCheckBoxStyle")));
            UiControlContract.AssertTemplate(checkbox,"RegularCheckBoxTemplate");
            Assert.That(checkbox.IsChecked,Is.EqualTo(!duplicate));
            var marker = Children(view.Root).OfType<TextBlock>().Single(t => t.Text == "Duplicated");
            Assert.That(marker.Visibility, Is.EqualTo(duplicate ? Visibility.Visible : Visibility.Collapsed));if(suppress) Assert.That(checkbox.IsTabStop,Is.False);
            var yes=(BalloonButton)popup.FindName("YesAction");UiControlContract.AssertStyle<BalloonButton>(yes,"OHsBalloonConfirmActionStyle");
            checkbox.IsChecked=false;Assert.That(vm.CanConfirm,Is.False);Assert.That(yes.IsEnabled,Is.False);
            checkbox.IsChecked=true;Assert.That(marker.Visibility, Is.EqualTo(duplicate ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(vm.TryCompleteSelection(true),Is.True);
            Assert.That(vm.SelectedPaths,Is.EqualTo(new[]{"drum-hitnormal9.wav"}));
        }
        finally { vm.Deactivate();popup.ReleaseOwner();popup.Close();app.Resources.MergedDictionaries[0]=original; }
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {yield return parent;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) foreach(var child in Children(VisualTreeHelper.GetChild(parent,i)))yield return child;}
}
