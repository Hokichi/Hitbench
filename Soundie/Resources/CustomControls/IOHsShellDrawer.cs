namespace Soundie.Resources.CustomControls;

public interface IOHsShellDrawer
{
    bool CanDismiss { get; }
    bool TryPrepareClose();
    event EventHandler? CloseRequested;
    void FocusInitialAction();
}
