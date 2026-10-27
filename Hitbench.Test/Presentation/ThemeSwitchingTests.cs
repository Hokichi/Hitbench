using System.Windows;
using Hitbench.Services.Ui;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class ThemeSwitchingTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void ShellBrandingFollowsInitialThemeSwitchesAndRollback(int initialTheme)
    {
        new SongsResponsiveLayoutTests().SetUp();
        var settings = new StubAppSettingsService();
        var service = new OHsThemeService(Application.Current.Resources, settings, new ImmediateUiScheduler());
        service.ApplyInitialTheme(initialTheme);
        var messenger = new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger();
        var vm = new Hitbench.ViewModels.Shell.HomePage.HomePageVM(new StubHomeFolderService(), settings,
            new StubInstallationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
        var page = new Hitbench.Views.Pages.HomePage(vm);
        var window = new MainWindow(page, messenger, () => throw new AssertionException("Unexpected navigation."),
            vm, settings, error => Assert.Fail(error),
            requestWindowState: _ => Assert.Fail("Unexpected native window-state request."),
            getWorkArea: _ => new Size(1920, 1080), themeService: service);
        try
        {
            var header = (Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader");
            UiTestPump.Layout(header, 900, 100);
            var logo = Descendants(header).OfType<System.Windows.Controls.Image>().Single();
            UiControlContract.AssertStyle<System.Windows.Controls.Image>(logo, "OHsShellHeaderLogoStyle");

            void AssertBranding(int theme)
            {
                UiTestPump.Run(window.Dispatcher.InvokeAsync(() => { },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
                var name = theme == 1 ? "logo-light" : "logo";
                Assert.Multiple(() =>
                {
                    Assert.That(window.Icon?.ToString(), Does.EndWith($"/Resources/{name}.ico"));
                    Assert.That(logo.Source?.ToString(), Does.EndWith($"/Resources/{name}.png"));
                });
            }

            AssertBranding(initialTheme);
            UiTestPump.Run(service.ChangeThemeAsync(1 - initialTheme, default));
            AssertBranding(1 - initialTheme);
            UiTestPump.Run(service.ChangeThemeAsync(initialTheme, default));
            AssertBranding(initialTheme);
            settings.SaveTheme = (_, _) =>
            {
                AssertBranding(1 - initialTheme);
                return Task.FromException(new IOException("disk failed"));
            };
            Assert.Throws<IOException>(() => UiTestPump.Run(service.ChangeThemeAsync(1 - initialTheme, default)));
            AssertBranding(initialTheme);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Test]
    public void BackgroundAndModalBrushesFollowSwitchAndRollback()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = new ResourceDictionary();
        var settings = new StubAppSettingsService();
        var service = new OHsThemeService(resources, settings, new ImmediateUiScheduler());
        service.ApplyInitialTheme(0);
        var host = new System.Windows.Controls.StackPanel { Resources = resources };
        var background = new System.Windows.Controls.Border();
        var modal = new System.Windows.Controls.Border();
        host.Children.Add(background); host.Children.Add(modal);
        background.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.BackgroundOverlay");
        modal.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.Overlay");
        void AssertResources()
        {
            Assert.That(resources["Brush.BackgroundOverlay"], Is.InstanceOf<System.Windows.Media.SolidColorBrush>());
            Assert.That(background.Background, Is.SameAs(resources["Brush.BackgroundOverlay"]));
            Assert.That(modal.Background, Is.SameAs(resources["Brush.Overlay"]));
            Assert.That(background.Background, Is.Not.SameAs(modal.Background));
        }
        AssertResources();
        UiTestPump.Run(service.ChangeThemeAsync(1, default)); AssertResources();
        settings.SaveTheme = (_, _) => Task.FromException(new IOException("disk failed"));
        Assert.Throws<IOException>(() => UiTestPump.Run(service.ChangeThemeAsync(0, default)));
        Assert.That(service.Theme, Is.EqualTo(1)); AssertResources();
    }
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
        resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/Hitbench;component/Resources/Themes/Dark.xaml",UriKind.Relative) });
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
