using Hitbench.Resources.CustomControls;
using System.Windows;
using System.Windows.Input;

namespace Hitbench.Test.Presentation;

public sealed class StubBalloonMenu : BalloonMenu
{
    public Rect Bounds { get; set; } = new(20, 30, 40, 40);
    public bool PointerInside { get; set; }
    public bool Animate { get; set; }
    public List<bool> PresentationRequests { get; } = [];

    public StubBalloonMenu() => PresentPopup = (_, open) => PresentationRequests.Add(open);
    protected override Rect CaptureLauncherBounds() => Bounds;
    protected override bool IsPointerWithinMenu() => PointerInside;
    protected override bool ClientAreaAnimationEnabled => Animate;
    public void Enter() => OnMouseEnter(new MouseEventArgs(Mouse.PrimaryDevice, 0));
    public void Leave() => OnMouseLeave(new MouseEventArgs(Mouse.PrimaryDevice, 0));
    public void MoveOwner() => RefreshPlacement();
}
