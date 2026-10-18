using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

public sealed class StubWorkstationTrackRow : OHsWorkstationTrackRow
{
    public BalloonComboBox? RequestedMenuOwner { get; private set; }
    protected override void OpenTrackActionMenu(BalloonComboBox trigger) => RequestedMenuOwner = trigger;
}
