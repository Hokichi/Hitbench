using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Hitbench.Services.Ui;

internal static class PopupPlacement
{
    internal static Rect? GetOwnerBounds(Window popup)
    {
        if (popup.Owner is not { IsVisible: true } owner) return null;
        var ownerHandle = new WindowInteropHelper(owner).Handle;
        var popupHandle = new WindowInteropHelper(popup).Handle;
        if (ownerHandle == 0 || popupHandle == 0 || HwndSource.FromHwnd(popupHandle)?.CompositionTarget is not { } target ||
            !GetWindowRect(ownerHandle, out var bounds) || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return null;
        return ToWpfBounds(new Rect(bounds.Left, bounds.Top, (double)bounds.Right - bounds.Left, (double)bounds.Bottom - bounds.Top), target.TransformFromDevice);
    }

    internal static Rect? ToWpfBounds(Rect screenBounds, Matrix fromDevice)
    {
        if (!ValidBounds(screenBounds) || !fromDevice.HasInverse ||
            !double.IsFinite(fromDevice.M11) || !double.IsFinite(fromDevice.M12) ||
            !double.IsFinite(fromDevice.M21) || !double.IsFinite(fromDevice.M22) ||
            !double.IsFinite(fromDevice.OffsetX) || !double.IsFinite(fromDevice.OffsetY)) return null;
        var bounds = Rect.Transform(screenBounds, fromDevice);
        return ValidBounds(bounds) ? bounds : null;
    }

    internal static Point GetCenteredPosition(Rect? ownerBounds, Point currentPosition, Size previousSize, Size newSize)
    {
        if (newSize.IsEmpty || !double.IsFinite(newSize.Width) || !double.IsFinite(newSize.Height)) return currentPosition;
        if (ownerBounds is { } owner && ValidBounds(owner))
            return new Point(owner.Left + (owner.Width - newSize.Width) / 2, owner.Top + (owner.Height - newSize.Height) / 2);
        if (previousSize.IsEmpty || !double.IsFinite(previousSize.Width) || !double.IsFinite(previousSize.Height)) return currentPosition;
        return new Point(currentPosition.X - (newSize.Width - previousSize.Width) / 2,
            currentPosition.Y - (newSize.Height - previousSize.Height) / 2);
    }

    private static bool ValidBounds(Rect bounds) => !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0 &&
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);
}
