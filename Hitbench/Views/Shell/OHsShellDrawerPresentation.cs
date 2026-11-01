using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Resources.DataModels;
using Hitbench.Views.Pages;

namespace Hitbench.Views.Shell;

internal sealed class OHsShellDrawerPresentation(ContentControl host, Grid motionHost)
{
    internal ContentControl Host { get; } = host;
    internal Grid MotionHost { get; } = motionHost;
    internal OHsSampleDrawerPhase Phase { get; set; }
    internal bool IsBottom { get; set; }
    internal TaskCompletionSource? Completion { get; set; }
    internal CancellationTokenSource? Cancellation { get; set; }
    internal Func<Task<bool>>? Close { get; set; }
    internal Action? ClearFocusRestore { get; set; }
    internal WorkstationPage? DockedPage { get; set; }
    internal Task Presentation { get; set; } = Task.CompletedTask;
    internal TranslateTransform Translation { get; } = new();
    internal TaskCompletionSource? AnimationCompletion { get; set; }
    internal bool AnimationOpening { get; set; }
}
