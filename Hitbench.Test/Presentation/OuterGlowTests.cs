using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Effects;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OuterGlowTests
{
    private Button source = null!;
    private ScrollViewer viewer = null!;
    private AdornerDecorator host = null!;
    private InMemoryView view = null!;
    private readonly DropShadowEffect effect = new() { BlurRadius = 7, ShadowDepth = 0 };

    [SetUp]
    public void SetUp()
    {
        new SongsResponsiveLayoutTests().SetUp();
        source = new Button { Content = "Card", Width = 80, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var content = new Grid { Height = 500 };
        content.Children.Add(source);
        viewer = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        host = new AdornerDecorator { Child = viewer };
        OuterGlow.SetIsHost(host, true);
        view = new InMemoryView(new Window { Content = host, Width = 120, Height = 80 });
    }

    [TearDown]
    public void TearDown()
    {
        OuterGlow.SetEffect(source, null);
        view.Dispose();
    }

    [Test]
    public void MarkedOuterHostOwnsGlowInsteadOfNativeInnerLayer()
    {
        var context = new object();
        source.DataContext = context;
        var executions = 0;
        var command = new RelayCommand(() => executions++);
        source.Command = command;
        OuterGlow.SetEffect(source, effect);
        view.UpdateLayout();
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
        var inner = AdornerLayer.GetAdornerLayer(source);
        Assert.That(inner, Is.Not.SameAs(host.AdornerLayer));
        Assert.That(inner.GetAdorners(source), Is.Null);
        Assert.That(host.AdornerLayer.GetAdorners(source)![0], Is.TypeOf<OuterGlowAdorner>());
        Assert.That(source.Effect, Is.Null);
        Assert.That(source.DataContext, Is.SameAs(context));
        Assert.That(source.Content, Is.EqualTo("Card"));
        Assert.That(source.Command, Is.SameAs(command));
        source.Command.Execute(null);
        Assert.That(executions, Is.EqualTo(1));
        Assert.That(host.AdornerLayer.GetAdorners(viewer), Is.Null);
    }

    [Test]
    public void RepeatedActivationDoesNotDuplicateAndClearRemoves()
    {
        OuterGlow.SetEffect(source, effect);
        for (var i = 0; i < 3; i++)
        {
            source.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            view.UpdateLayout();
        }
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
        OuterGlow.SetEffect(source, null);
        view.UpdateLayout();
        Assert.That(host.AdornerLayer.GetAdorners(source), Is.Null);
    }

    [Test]
    public void UnloadReloadAndDataContextChangesReleaseOldAdornment()
    {
        OuterGlow.SetEffect(source, effect);
        var old = host.AdornerLayer.GetAdorners(source)!.Single();
        source.DataContext = new object();
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
        Assert.That(host.AdornerLayer.GetAdorners(source)![0], Is.Not.SameAs(old));
        source.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        view.UpdateLayout();
        Assert.That(host.AdornerLayer.GetAdorners(source), Is.Null);
        source.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
    }

    [Test]
    public void HostDeactivationRemovesAndReactivationRestoresOnce()
    {
        OuterGlow.SetEffect(source, effect);
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
        OuterGlow.SetIsActive(host, false);
        view.UpdateLayout();
        Assert.That(host.AdornerLayer.GetAdorners(source), Is.Null);
        OuterGlow.SetIsActive(host, true);
        view.UpdateLayout();
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
    }

    [Test]
    public void MissingHostAndOffscreenSourcesCreateNoAdornment()
    {
        OuterGlow.SetIsHost(host, false);
        OuterGlow.SetEffect(source, effect);
        Assert.That(host.AdornerLayer.GetAdorners(source), Is.Null);
        OuterGlow.SetEffect(source, null);
        OuterGlow.SetIsHost(host, true);
        OuterGlow.SetEffect(source, effect);
        viewer.ScrollToVerticalOffset(200);
        view.UpdateLayout();
        Assert.That(host.AdornerLayer.GetAdorners(source), Is.Null);
        viewer.ScrollToTop();
        view.UpdateLayout();
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
    }

    [Test]
    public void ClearedEffectCanActivateAfterReload()
    {
        OuterGlow.SetEffect(source, effect);
        source.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        OuterGlow.SetEffect(source, null);
        source.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        OuterGlow.SetEffect(source, effect);
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
    }

    [Test]
    public void DisabledSourceRemovesAndRestoresWithoutDuplicate()
    {
        OuterGlow.SetEffect(source, effect);
        source.IsEnabled = false;
        Assert.That(host.AdornerLayer.GetAdorners(source), Is.Null);
        source.IsEnabled = true;
        Assert.That(host.AdornerLayer.GetAdorners(source), Has.Length.EqualTo(1));
    }
}
