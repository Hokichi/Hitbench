namespace osuHitsounder.Resources.CustomControls;

public interface IPopupHost
{
    void ShowPopupOverlay();
    void HidePopupOverlay();
    void HidePopupOverlayForHandoff();
    void BeginPopupHandoff();
}
