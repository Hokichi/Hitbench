using System.ComponentModel;
using Hitbench.Views.Shell;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Hitbench;

public partial class MainWindow
{
    internal Task RunSampleManagerAnimationAsync(bool opening, bool animationsEnabled, CancellationToken token) =>
        RunDrawerAnimationAsync(sampleDrawerPresentation, opening, animationsEnabled, token);

    internal Task RunDrawerAnimationAsync(OHsShellDrawerPresentation state, bool opening, bool animationsEnabled, CancellationToken token)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled(token);
        state.AnimationCompletion?.TrySetCanceled();
        ClearDrawerAnimationClocks(state);
        state.MotionHost.RenderTransform = state.Translation;
        if (!animationsEnabled)
        {
            state.AnimationCompletion = null;
            ApplyDrawerAnimationEnd(state, opening);
            return Task.CompletedTask;
        }
        if (opening)
        {
            state.Translation.X = state.IsBottom ? 0 : 24;
            state.Translation.Y = state.IsBottom ? 24 : 0;
            state.Host.Opacity = 0;
            if (!state.IsBottom) SampleManagerBackdrop.Opacity = 0;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.AnimationCompletion = completion;
        state.AnimationOpening = opening;
        var easing = new CubicEase { EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(180);
        var axis = state.IsBottom ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        var movement = new DoubleAnimation(state.IsBottom ? state.Translation.Y : state.Translation.X, opening ? 0 : 24, duration) { EasingFunction = easing };
        var fade = new DoubleAnimation(state.Host.Opacity, opening ? 1 : 0, duration) { EasingFunction = easing };
        var backdropFade = new DoubleAnimation(SampleManagerBackdrop.Opacity, opening ? 1 : 0, duration) { EasingFunction = easing };
        state.DockedPage?.BeginDrawerHostAnimation(state.MotionHost, opening, movement.Duration, easing);
        void OnCompleted(object? sender, EventArgs args)
        {
            if (ReferenceEquals(state.AnimationCompletion, completion)) SettleDrawerAnimation(state);
        }
        movement.Completed += OnCompleted;
        state.Translation.BeginAnimation(axis, movement);
        state.Host.BeginAnimation(OpacityProperty, fade);
        if (!state.IsBottom) SampleManagerBackdrop.BeginAnimation(OpacityProperty, backdropFade);
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
                    if (!ReferenceEquals(state.AnimationCompletion, completion)) return;
                    ClearDrawerAnimationClocks(state);
                    if (completion.Task.IsCompletedSuccessfully) ApplyDrawerAnimationEnd(state, opening);
                    state.AnimationCompletion = null;
                });
            }
        }
    }

    internal void SettleSampleManagerAnimation() => SettleDrawerAnimation(sampleDrawerPresentation);

    internal void SettleDrawerAnimation(OHsShellDrawerPresentation state)
    {
        if (state.AnimationCompletion is not { } completion) return;
        ClearDrawerAnimationClocks(state);
        ApplyDrawerAnimationEnd(state, state.AnimationOpening);
        completion.TrySetResult();
    }

    private void ClearDrawerAnimationClocks(OHsShellDrawerPresentation state)
    {
        state.DockedPage?.ClearDrawerHostAnimation(state.MotionHost);
        var shift = state.Translation.X;
        var verticalShift = state.Translation.Y;
        var opacity = state.Host.Opacity;
        var backdropOpacity = SampleManagerBackdrop.Opacity;
        state.Translation.BeginAnimation(TranslateTransform.XProperty, null);
        state.Translation.BeginAnimation(TranslateTransform.YProperty, null);
        state.Host.BeginAnimation(OpacityProperty, null);
        if (!state.IsBottom) SampleManagerBackdrop.BeginAnimation(OpacityProperty, null);
        state.Translation.X = shift;
        state.Translation.Y = verticalShift;
        state.Host.Opacity = opacity;
        if (!state.IsBottom) SampleManagerBackdrop.Opacity = backdropOpacity;
    }

    private void ApplyDrawerAnimationEnd(OHsShellDrawerPresentation state, bool opening)
    {
        state.DockedPage?.SettleDrawerHostAnimation(state.MotionHost, opening);
        state.Translation.X = state.IsBottom ? 0 : opening ? 0 : 24;
        state.Translation.Y = state.IsBottom && !opening ? 24 : 0;
        state.Host.Opacity = opening ? 1 : 0;
        if (!state.IsBottom) SampleManagerBackdrop.Opacity = opening ? 1 : 0;
    }

    private void ResetDrawerAnimation(OHsShellDrawerPresentation state)
    {
        state.AnimationCompletion?.TrySetCanceled();
        state.AnimationCompletion = null;
        ClearDrawerAnimationClocks(state);
        ApplyDrawerAnimationEnd(state, true);
    }

    private async void OnSampleManagerAnimationSettingChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.ClientAreaAnimation) || SystemParameters.ClientAreaAnimation || Dispatcher.HasShutdownStarted) return;
        try { await Dispatcher.InvokeAsync(() => { SettleDrawerAnimation(sampleDrawerPresentation); SettleDrawerAnimation(InstrumentalDrawerPresentation); }); }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Drawer animation setting update failed: {0}", error); }
    }
}
