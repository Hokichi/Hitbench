namespace Onset.Resources.CustomControls;

public sealed class PopupOverlayHandoffState
{
    private int _activePopupCount;
    private int _deferredHideGeneration;
    private int? _pendingDeferredHideGeneration;

    public int ActivePopupCount => _activePopupCount;

    public PopupOverlayHostAction OnPopupShown()
    {
        if (_pendingDeferredHideGeneration.HasValue)
        {
            _pendingDeferredHideGeneration = null;
            _activePopupCount++;
            return PopupOverlayHostAction.None;
        }

        _activePopupCount++;
        return _activePopupCount == 1
            ? PopupOverlayHostAction.ShowOverlay
            : PopupOverlayHostAction.None;
    }

    public PopupOverlayHostAction OnPopupHidden()
    {
        if (_activePopupCount == 0)
            return PopupOverlayHostAction.None;

        _activePopupCount--;
        if (_activePopupCount > 0)
            return PopupOverlayHostAction.None;

        _pendingDeferredHideGeneration = null;
        return PopupOverlayHostAction.HideOverlay;
    }

    public PopupOverlayHostAction OnPopupHiddenForHandoff(out int deferredHideGeneration)
    {
        deferredHideGeneration = 0;

        if (_activePopupCount == 0)
            return PopupOverlayHostAction.None;

        _activePopupCount--;
        if (_activePopupCount > 0)
            return PopupOverlayHostAction.None;

        _deferredHideGeneration++;
        _pendingDeferredHideGeneration = _deferredHideGeneration;
        deferredHideGeneration = _deferredHideGeneration;
        return PopupOverlayHostAction.DeferHide;
    }

    public PopupOverlayHostAction ResolveDeferredHide(int deferredHideGeneration)
    {
        if (_pendingDeferredHideGeneration is null)
            return PopupOverlayHostAction.None;

        if (_pendingDeferredHideGeneration.Value != deferredHideGeneration)
            return PopupOverlayHostAction.None;

        _pendingDeferredHideGeneration = null;
        return _activePopupCount == 0
            ? PopupOverlayHostAction.HideOverlay
            : PopupOverlayHostAction.None;
    }
}

