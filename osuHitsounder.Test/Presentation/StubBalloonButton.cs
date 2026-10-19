using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

public sealed class StubBalloonButton : BalloonButton
{
    public object? CurrentIcon => ResolveButtonIcon();
    public string? CurrentText => ResolveButtonText();
    public void Invoke() => OnClick();
}
