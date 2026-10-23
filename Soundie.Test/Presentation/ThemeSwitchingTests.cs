using System.Windows;
using Soundie.Services.Ui;
namespace Soundie.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class ThemeSwitchingTests
{
    [Test]
    public void RapidChangesAreSerializedAndLastSuccessfulChoiceWins()
    {
        var app=Application.Current ?? new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var resources=new ResourceDictionary(); var settings=new StubAppSettingsService();
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved=new List<int>(); var started=new TaskCompletionSource();
        settings.SaveTheme=async (value,_)=>{if(saved.Count==0) { started.TrySetResult(); await release.Task; } saved.Add(value);};
        var service=new OHsThemeService(resources,settings,new UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        service.ApplyInitialTheme(0);
        var first=service.ChangeThemeAsync(1,default);var second=service.ChangeThemeAsync(0,default);
        UiTestPump.Run(started.Task);
        Assert.That(service.IsChangingTheme,Is.True);Assert.That(second.IsCompleted,Is.False);
        release.SetResult();UiTestPump.Run(Task.WhenAll(first,second));
        Assert.That(saved,Is.EqualTo(new[]{1,0}));Assert.That(service.Theme,Is.Zero);
    }
    [Test]
    public void SwitchPreservesResourcesAndRollsBackFailedSave()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources=new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/Soundie;component/Resources/Themes/Dark.xaml",UriKind.Relative) });
        var sentinel=new object(); resources["sentinel"]=sentinel;
        var settings=new StubAppSettingsService();
        var saved=new List<int>(); settings.SaveTheme=(value,_)=>{saved.Add(value);return Task.CompletedTask;};
        var service=new OHsThemeService(resources,settings,new ImmediateUiScheduler());
        service.ApplyInitialTheme(0);
        UiTestPump.Run(service.ChangeThemeAsync(1,default));
        Assert.That(service.Theme,Is.EqualTo(1)); Assert.That(service.IsLightTheme,Is.True);
        Assert.That(resources["sentinel"],Is.SameAs(sentinel)); Assert.That(saved,Is.EqualTo(new[]{1}));
        settings.SaveTheme=(_,_)=>Task.FromException(new IOException("disk failed"));
        Assert.Throws<IOException>(()=>UiTestPump.Run(service.ChangeThemeAsync(0,default)));
        Assert.That(service.Theme,Is.EqualTo(1)); Assert.That(service.IsChangingTheme,Is.False);
    }
}
