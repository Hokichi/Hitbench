using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using LottieSharp.WPF;

namespace Hitbench.Resources.CustomControls;

/// <summary>Keeps decorative playback failures outside the mapset-loading workflow.</summary>
public sealed class OHsScanningAnimationView : LottieAnimationView
{
    public event EventHandler? PlaybackFailed;

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Layout may run while the visual has no rendering target, including during detachment.
        if (PresentationSource.FromVisual(this)?.CompositionTarget is null) return;
        try { base.OnRender(drawingContext); }
        catch (Exception error)
        {
            Trace.TraceError("Mapset scan rendering: {0}", error);
            StopAnimation();
            PlaybackFailed?.Invoke(this, EventArgs.Empty);
        }
    }
}
