using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace osuHitsounder;

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
            sampleManagerTranslation.X = 24;
            SampleManagerHost.Opacity = 0;
            SampleManagerBackdrop.Opacity = 0;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sampleManagerAnimationCompletion = completion;
        sampleManagerAnimationOpening = opening;
        var easing = new CubicEase { EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(180);
        var movement = new DoubleAnimation(sampleManagerTranslation.X, opening ? 0 : 24, duration) { EasingFunction = easing };
        var fade = new DoubleAnimation(SampleManagerHost.Opacity, opening ? 1 : 0, duration) { EasingFunction = easing };
        var backdropFade = new DoubleAnimation(SampleManagerBackdrop.Opacity, opening ? 1 : 0, duration) { EasingFunction = easing };
        void OnCompleted(object? sender, EventArgs args)
        {
            if (ReferenceEquals(sampleManagerAnimationCompletion, completion)) SettleSampleManagerAnimation();
        }
        movement.Completed += OnCompleted;
        sampleManagerTranslation.BeginAnimation(TranslateTransform.XProperty, movement);
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
        var shift = sampleManagerTranslation.X;
        var opacity = SampleManagerHost.Opacity;
        var backdropOpacity = SampleManagerBackdrop.Opacity;
        sampleManagerTranslation.BeginAnimation(TranslateTransform.XProperty, null);
        SampleManagerHost.BeginAnimation(OpacityProperty, null);
        SampleManagerBackdrop.BeginAnimation(OpacityProperty, null);
        sampleManagerTranslation.X = shift;
        SampleManagerHost.Opacity = opacity;
        SampleManagerBackdrop.Opacity = backdropOpacity;
    }

    private void ApplySampleManagerAnimationEnd(bool opening)
    {
        sampleManagerTranslation.X = opening ? 0 : 24;
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
