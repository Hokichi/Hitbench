namespace Soundie.Resources.CustomControls;

public interface IPopupHost
{
    void ShowPopupOverlay();
    void ShowPopupOverlay(double opacity, bool useBlur) => ShowPopupOverlay();
    void HidePopupOverlay();
    void HidePopupOverlayForHandoff();
    void BeginPopupHandoff();
}
