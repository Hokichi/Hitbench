using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;

namespace Hitbench.Test.Presentation;

/// <summary>Measures window content without showing the window or creating an HWND.</summary>
public sealed class InMemoryView : IDisposable
{
    private readonly InMemoryPresentationSource source = new();
    private readonly DispatcherTimer layout;
    private readonly Window window;
    private readonly HashSet<FrameworkElement> attached = [];
    private bool disposed;
    private long frameNumber;
    private readonly SynchronizationContext? previousContext;
    private readonly DispatcherSynchronizationContext context;
    private readonly bool processFrames;
    private readonly bool inheritsFlowDirection;
    public FrameworkElement Root { get; }

    public InMemoryView(Window window, bool processFrames = true, bool scheduleLayout = true)
    {
        this.processFrames = processFrames;
        previousContext = SynchronizationContext.Current;
        context = new DispatcherSynchronizationContext(window.Dispatcher);
        SynchronizationContext.SetSynchronizationContext(context);
        this.window = window;
        Root = (FrameworkElement)window.Content;
        inheritsFlowDirection = Root.ReadLocalValue(FrameworkElement.FlowDirectionProperty) == DependencyProperty.UnsetValue;
        window.Content = null;
        Root.DataContext = window.DataContext ?? Root.DataContext;
        source.RootVisual = Root;
        UpdateLayout();
        layout = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) => UpdateLayout(), window.Dispatcher);
        if (!scheduleLayout) layout.Stop();
    }

    public void UpdateLayout()
    {
        if (inheritsFlowDirection)
            Root.SetCurrentValue(FrameworkElement.FlowDirectionProperty, window.FlowDirection);
        UiTestPump.Layout(Root, double.IsFinite(window.Width) ? window.Width : 1280,
            double.IsFinite(window.Height) ? window.Height : 720);
        var current = Descendants(Root).ToHashSet();
        foreach (var removed in attached.Except(current).ToArray())
        {
            attached.Remove(removed);
            removed.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, removed));
        }
        foreach (var element in current)
            if (attached.Add(element))
                element.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, element));
        if (processFrames)
            foreach (var page in current.OfType<Hitbench.Views.Pages.WorkstationPage>())
                page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(++frameNumber * 16));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        layout.Stop();
        foreach (var element in attached)
            element.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, element));
        attached.Clear();
        source.Dispose();
        if (ReferenceEquals(SynchronizationContext.Current, context)) SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        if (parent is FrameworkElement element) yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i)))
                yield return child;
    }
}
