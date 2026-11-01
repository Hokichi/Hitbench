using System.Windows.Controls;
using System.Windows.Input;
namespace Hitbench.Resources.CustomControls;

public class OHsTrackIdentityButton : Button
{
    private bool pointerRelease;
    private int pointerClickCount;
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        pointerClickCount = e.ClickCount;
        base.OnMouseLeftButtonDown(e);
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        pointerRelease = true;
        try { base.OnMouseLeftButtonUp(e); }
        finally { pointerRelease = false; }
    }
    protected override void OnClick()
    {
        if (pointerRelease) HandlePointerActivation(pointerClickCount);
        else base.OnClick();
    }
    internal bool HandlePointerActivation(int clickCount)
    {
        if (!IsEnabled || clickCount != 2) return false;
        base.OnClick();
        return true;
    }
}
