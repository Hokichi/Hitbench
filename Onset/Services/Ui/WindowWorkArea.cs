using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Onset.Services.Ui;

internal static class WindowWorkArea
{
    public static Size GetClientSize(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || HwndSource.FromHwnd(handle)?.CompositionTarget is not { } target) return Size.Empty;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return Size.Empty;
        // The borderless native frame makes maximized content fill the monitor's work area.
        var pixels = new Vector(info.WorkArea.Right - info.WorkArea.Left, info.WorkArea.Bottom - info.WorkArea.Top);
        var dips = target.TransformFromDevice.Transform(pixels);
        return new Size(dips.X, dips.Y);
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);
}
