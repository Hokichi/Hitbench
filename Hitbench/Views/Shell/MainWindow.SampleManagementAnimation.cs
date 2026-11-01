using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Hitbench;

public partial class MainWindow
{
    private readonly TranslateTransform sampleManagerTranslation = new();
    private TaskCompletionSource? sampleManagerAnimationCompletion;
    private bool sampleManagerAnimationOpening;

    private Task AnimateSampleManagerAsync(bool opening, CancellationToken token) =>
        RunSampleManagerAnimationAsync(opening, SystemParameters.ClientAreaAnimation, token);

    internal Task RunSampleManagerAnimationAsync(bool opening, bool animationsEnabled, CancellationToken token)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled(token);
        sampleManagerAnimationCompletion?.TrySetCanceled();
        ClearSampleManagerAnimationClocks();
        SampleManagerMotionHost.RenderTransform = sampleManagerTranslation;
        if (!animationsEnabled)
        {
            sampleManagerAnimationCompletion = null;
            ApplySampleManagerAnimationEnd(opening);
            return Task.CompletedTask;
        }
        if (opening)
        {
            sampleManagerTranslation.X = IsBottomDrawer ? 0 : 24;
            sampleManagerTranslation.Y = IsBottomDrawer ? 24 : 0;
            SampleManagerHost.Opacity = 0;
            SampleManagerBackdrop.Opacity = 0;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sampleManagerAnimationCompletion = completion;
        sampleManagerAnimationOpening = opening;
        var easing = new CubicEase { EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(180);
        var axis = IsBottomDrawer ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        var movement = new DoubleAnimation(IsBottomDrawer ? sampleManagerTranslation.Y : sampleManagerTranslation.X, opening ? 0 : 24, duration) { EasingFunction = easing };
        var fade = new DoubleAnimation(SampleManagerHost.Opacity, opening ? 1 : 0, duration) { EasingFunction = easing };
        var backdropFade = new DoubleAnimation(SampleManagerBackdrop.Opacity, opening ? 1 : 0, duration) { EasingFunction = easing };
        dockedDrawerPage?.BeginDrawerHostAnimation(SampleManagerMotionHost, opening, movement.Duration, easing);
        void OnCompleted(object? sender, EventArgs args)
        {
            if (ReferenceEquals(sampleManagerAnimationCompletion, completion)) SettleSampleManagerAnimation();
        }
        movement.Completed += OnCompleted;
        sampleManagerTranslation.BeginAnimation(axis, movement);
        SampleManagerHost.BeginAnimation(OpacityProperty, fade);
        SampleManagerBackdrop.BeginAnimation(OpacityProperty, backdropFade);
        return FinishAsync();

        async Task FinishAsync()
        {
            using var cancellation = token.Register(() => completion.TrySetCanceled(token));
            try { await completion.Task.ConfigureAwait(false); }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    movement.Completed -= OnCompleted;
                    if (!ReferenceEquals(sampleManagerAnimationCompletion, completion)) return;
                    ClearSampleManagerAnimationClocks();
                    if (completion.Task.IsCompletedSuccessfully) ApplySampleManagerAnimationEnd(opening);
                    sampleManagerAnimationCompletion = null;
                });
            }
        }
    }

    internal void SettleSampleManagerAnimation()
    {
        if (sampleManagerAnimationCompletion is not { } completion) return;
        ClearSampleManagerAnimationClocks();
        ApplySampleManagerAnimationEnd(sampleManagerAnimationOpening);
        completion.TrySetResult();
    }

    private void ClearSampleManagerAnimationClocks()
    {
        dockedDrawerPage?.ClearDrawerHostAnimation(SampleManagerMotionHost);
        var shift = sampleManagerTranslation.X;
        var verticalShift = sampleManagerTranslation.Y;
        var opacity = SampleManagerHost.Opacity;
        var backdropOpacity = SampleManagerBackdrop.Opacity;
        sampleManagerTranslation.BeginAnimation(TranslateTransform.XProperty, null);
        sampleManagerTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        SampleManagerHost.BeginAnimation(OpacityProperty, null);
        SampleManagerBackdrop.BeginAnimation(OpacityProperty, null);
        sampleManagerTranslation.X = shift;
        sampleManagerTranslation.Y = verticalShift;
        SampleManagerHost.Opacity = opacity;
        SampleManagerBackdrop.Opacity = backdropOpacity;
    }

    private void ApplySampleManagerAnimationEnd(bool opening)
    {
        dockedDrawerPage?.SettleDrawerHostAnimation(SampleManagerMotionHost, opening);
        sampleManagerTranslation.X = IsBottomDrawer ? 0 : opening ? 0 : 24;
        sampleManagerTranslation.Y = IsBottomDrawer && !opening ? 24 : 0;
        SampleManagerHost.Opacity = opening ? 1 : 0;
        SampleManagerBackdrop.Opacity = opening ? 1 : 0;
    }

    private void ResetSampleManagerAnimation()
    {
        sampleManagerAnimationCompletion?.TrySetCanceled();
        sampleManagerAnimationCompletion = null;
        ClearSampleManagerAnimationClocks();
        ApplySampleManagerAnimationEnd(true);
    }

    private async void OnSampleManagerAnimationSettingChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.ClientAreaAnimation) || SystemParameters.ClientAreaAnimation || Dispatcher.HasShutdownStarted) return;
        try { await Dispatcher.InvokeAsync(SettleSampleManagerAnimation); }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Drawer animation setting update failed: {0}", error); }
    }
}
