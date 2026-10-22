using System.Windows;
using System.Windows.Media;

namespace Soundie.Test.Presentation;

/// <summary>A presentation root for routed input; never creates a native window or renders.</summary>
public sealed class InMemoryPresentationSource : PresentationSource, IDisposable
{
    private Visual? root;
    private bool disposed;
    public override Visual RootVisual
    {
        get => root!;
        set
        {
            var previous = root;
            root = value;
            RootChanged(previous, root);
        }
    }

    public override bool IsDisposed => disposed;
    protected override CompositionTarget GetCompositionTargetCore() => null!;
    public void Dispose()
    {
        if (disposed) return;
        RootVisual = null!;
        disposed = true;
    }
}
