using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class HomePageIntegrationTests
{
    [Test]
    public void ClosingShellCancelsOwnedLoad()
    {
        EnsureResources();
        var pending = new TaskCompletionSource<HomeFolderData>();
        var vm = new HomePageVM(new StubHomeFolderService(token => pending.Task.WaitAsync(token)));
        var window = new MainWindow(new HomePage(vm));
        window.Show();
        var load = window.LoadAsync();
        window.Close();
        var frame = new DispatcherFrame();
        load.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        Assert.That(load.IsCanceled, Is.True);
        Assert.That(vm.IsLoading, Is.False);
    }

    [Test]
    public void ContainerBuildsShellWithInjectedPageAndViewModel()
    {
        EnsureResources();
        using var provider = App.CreateServices();
        var window = provider.GetRequiredService<MainWindow>();
        var page = provider.GetRequiredService<HomePage>();
        Assert.That(((ContentControl)window.Content).Content, Is.SameAs(page));
        Assert.That(page.DataContext, Is.SameAs(provider.GetRequiredService<HomePageVM>()));
        window.Close();
    }

    private static void EnsureResources()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        foreach (var resource in new[] { "Themes/Dark.xaml", "Fonts/Fonts.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }

    [Test]
    public void PanelsBindAndVirtualizeAcrossSizesAndThemes()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var trace = new StringBuilder();
        using var writer = new StringWriter(trace);
        using var listener = new TextWriterTraceListener(writer);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var resource in new[] { $"Themes/{theme}.xaml", "Fonts/Fonts.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
                    });

                var folders = Enumerable.Range(0, 2000).Select(i => new DirectoryInfo($"C:\\Songs\\{i:D4} long folder name")).ToArray();
                var vm = new HomePageVM(new StubHomeFolderService(_ => Task.FromResult(
                    new HomeFolderData([new DirectoryInfo("C:\\Projects\\One")], folders, "C:\\osu", true, false))));
                var page = new HomePage(vm);
                page.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var window = new Window { Content = page, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                try
                {
                    window.Show();
                    foreach (var width in new[] { 800d, 1280d })
                    {
                        window.Width = width;
                        window.Height = 450;
                        window.UpdateLayout();
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                        var lists = Descendants<ItemsControl>(page).ToArray();
                        Assert.That(lists, Has.Length.EqualTo(2));
                        Assert.That(lists[0].Items.Count, Is.EqualTo(1));
                        Assert.That(lists[1].Items.Count, Is.EqualTo(2000));
                        var columns = ((Grid)page.Content).ColumnDefinitions;
                        Assert.That(columns[1].ActualWidth / columns[0].ActualWidth, Is.EqualTo(3).Within(0.01));
                        var panel = Descendants<VirtualizingStackPanel>(lists[1]).Single();
                        Assert.That(panel.Children.Count, Is.InRange(1, 100));
                        var scroll = Descendants<ScrollViewer>(lists[1]).Single();
                        scroll.ScrollToEnd();
                        window.UpdateLayout();
                        Assert.That(scroll.VerticalOffset, Is.GreaterThan(0));
                        Assert.That(panel.Children.Count, Is.LessThan(100));
                    }
                }
                finally { window.Close(); }
            }
            listener.Flush();
            Assert.That(trace.ToString(), Is.Empty);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}

