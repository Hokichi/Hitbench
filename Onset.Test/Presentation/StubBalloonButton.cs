using Onset.Resources.CustomControls;

namespace Onset.Test.Presentation;

public sealed class StubBalloonButton : BalloonButton
{
    public object? CurrentIcon => ResolveButtonIcon();
    public string? CurrentText => ResolveButtonText();
    public void Invoke() => OnClick();
}
