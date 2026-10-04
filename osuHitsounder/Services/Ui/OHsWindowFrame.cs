using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace osuHitsounder.Services.Ui;

/// <summary>Owns the non-layered shell's DWM material and borderless native frame.</summary>
internal sealed class OHsWindowFrame : IDisposable
{
    private readonly Window window;
    private readonly Action<bool> publish;
    private readonly Func<nint, bool> enable;
    private readonly Action<nint> disable;
    private readonly Func<nint, int, int, int> setWindowAttribute;
    private HwndSource? source;
    private nint handle;
    private bool disposed;

    internal OHsWindowFrame(Window window, Action<bool> setAcrylicEnabled,
        Func<nint, bool>? enableBackdrop = null, Action<nint>? disableBackdrop = null,
        Func<nint, int, int, int>? setWindowAttribute = null)
    {
        this.window = window;
        publish = setAcrylicEnabled;
        enable = enableBackdrop ?? EnableBackdrop;
        disable = disableBackdrop ?? DisableBackdrop;
        this.setWindowAttribute = setWindowAttribute ?? SetWindowAttribute;
        window.SourceInitialized += OnSourceInitialized;
        window.Activated += OnEnvironmentChanged;
        window.StateChanged += OnEnvironmentChanged;
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(window).Handle;
        source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProc);
        // Keep WPF's existing flags; supply resizing/system-menu capabilities absent from WindowStyle.None.
        var style = GetWindowLong(handle, -16);
        SetWindowLong(handle, -16, style | 0x00080000 | 0x00020000 | 0x00010000 |
            (window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip ? 0x00040000 : 0));
        SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0027); // FRAMECHANGED, NOMOVE, NOSIZE, NOZORDER
        Refresh();
    }

    private void Refresh() => RefreshBackdrop(handle, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621),
        SystemParameters.HighContrast, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
    private void OnEnvironmentChanged(object? sender, EventArgs e) => Refresh();
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (window.Dispatcher.CheckAccess()) Refresh();
        else window.Dispatcher.BeginInvoke(new Action(Refresh));
    }

    internal void RefreshBackdrop(nint windowHandle, bool supported, bool highContrast, bool borderSupported = true)
    {
        if (disposed) return;
        handle = windowHandle;
        var enabled = false;
        try { enabled = handle != 0 && supported && !highContrast && enable(handle); }
        catch (ExternalException) { }
        if (!enabled && handle != 0) disable(handle);
        if (handle != 0 && borderSupported)
        {
            // ShellRoot owns the visible border, including the high-contrast fallback.
            try { setWindowAttribute(handle, 34, unchecked((int)0xFFFFFFFE)); } // DWMWA_BORDER_COLOR / COLOR_NONE
            catch (ExternalException) { }
        }
        if (source?.CompositionTarget is { } target)
            target.BackgroundColor = enabled ? Colors.Transparent : GetMainColor();
        publish(enabled);
    }

    private Color GetMainColor() => window.TryFindResource("Color.Background.Main") is Color color ? color : SystemColors.WindowColor;

    private static int SetWindowAttribute(nint hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    private bool EnableBackdrop(nint hwnd)
    {
        var color = GetMainColor();
        var dark = color.R + color.G + color.B < 384 ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var material = 3; // DWMSBT_TRANSIENTWINDOW: Desktop Acrylic
        if (DwmSetWindowAttribute(hwnd, 38, ref material, sizeof(int)) < 0) return false;
        var margins = new NativeMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        return DwmExtendFrameIntoClientArea(hwnd, ref margins) >= 0;
    }

    private static void DisableBackdrop(nint hwnd)
    {
        var material = 1;
        DwmSetWindowAttribute(hwnd, 38, ref material, sizeof(int));
        var margins = new NativeMargins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    internal nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0083) // WM_NCCALCSIZE: both forms retain the full proposed window rectangle.
        {
            handled = true;
            return 0;
        }
        if (message == 0x0084 && GetWindowRect(hwnd, out var bounds)) // WM_NCHITTEST
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var x = (short)((long)lParam & 0xffff);
            var y = (short)(((long)lParam >> 16) & 0xffff);
            var hit = GetResizeHitTest(new Point((x - bounds.Left) / dpi.DpiScaleX, (y - bounds.Top) / dpi.DpiScaleY),
                new Size((bounds.Right - bounds.Left) / dpi.DpiScaleX, (bounds.Bottom - bounds.Top) / dpi.DpiScaleY),
                window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip, window.WindowState == WindowState.Maximized);
            if (hit != 1) { handled = true; return hit; }
        }
        if (message == 0x0024) // WM_GETMINMAXINFO
        {
            var monitor = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
            if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor))
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                var info = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
                var computed = GetMaximizeInfo(monitor.Monitor, monitor.WorkArea, new Size(window.MinWidth, window.MinHeight), dpi.DpiScaleX, dpi.DpiScaleY);
                info.MaxPosition = computed.MaxPosition;
                info.MaxSize = computed.MaxSize;
                info.MinTrackSize = computed.MinTrackSize;
                info.MaxTrackSize.X = Math.Max(info.MaxTrackSize.X, computed.MaxSize.X);
                info.MaxTrackSize.Y = Math.Max(info.MaxTrackSize.Y, computed.MaxSize.Y);
                Marshal.StructureToPtr(info, lParam, false);
                handled = true;
            }
        }
        if (message is 0x031e or 0x001a or 0x031a) Refresh(); // composition/settings/theme
        return 0;
    }

    internal static int GetResizeHitTest(Point p, Size size, bool canResize, bool maximized)
    {
        if (!canResize || maximized || size.IsEmpty || size.Width <= 0 || size.Height <= 0 ||
            !double.IsFinite(size.Width) || !double.IsFinite(size.Height) || p.X < 0 || p.Y < 0 || p.X >= size.Width || p.Y >= size.Height) return 1;
        var left = p.X < 8;
        var right = p.X >= size.Width - 8;
        var top = p.Y < 8;
        var bottom = p.Y >= size.Height - 8;
        return top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : right ? 11 : 1;
    }

    internal static NativeMinMaxInfo GetMaximizeInfo(NativeRect monitor, NativeRect workArea, Size minimumDips, double dpiX, double dpiY) => new()
    {
        MaxPosition = new NativePoint { X = workArea.Left - monitor.Left, Y = workArea.Top - monitor.Top },
        MaxSize = new NativePoint { X = workArea.Right - workArea.Left, Y = workArea.Bottom - workArea.Top },
        MinTrackSize = new NativePoint { X = (int)Math.Ceiling(minimumDips.Width * dpiX), Y = (int)Math.Ceiling(minimumDips.Height * dpiY) }
    };

    internal bool TryGetDragGeometry(out Point cursorPixels, out Rect windowPixels, out double dpiX, out double dpiY)
    {
        cursorPixels = default;
        windowPixels = Rect.Empty;
        var dpi = VisualTreeHelper.GetDpi(window);
        dpiX = dpi.DpiScaleX;
        dpiY = dpi.DpiScaleY;
        if (disposed || handle == 0 || !GetCursorPos(out var cursor) || !GetWindowRect(handle, out var bounds)) return false;
        cursorPixels = new Point(cursor.X, cursor.Y);
        windowPixels = new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        return true;
    }

    internal void MoveRestoredWindow(Rect boundsPixels)
    {
        if (!disposed && handle != 0 && !boundsPixels.IsEmpty)
            SetWindowPos(handle, 0, (int)Math.Round(boundsPixels.X), (int)Math.Round(boundsPixels.Y),
                (int)Math.Round(boundsPixels.Width), (int)Math.Round(boundsPixels.Height), 0x0014); // NOZORDER, NOACTIVATE
    }

    internal static Rect GetRestoreBounds(Point cursorPixels, Rect maximizedPixels, Size restoredDips, double dpiX, double dpiY)
    {
        if (maximizedPixels.IsEmpty || maximizedPixels.Width <= 0 || maximizedPixels.Height <= 0 ||
            restoredDips.IsEmpty || restoredDips.Width <= 0 || restoredDips.Height <= 0 || dpiX <= 0 || dpiY <= 0) return Rect.Empty;
        var width = restoredDips.Width * dpiX;
        var height = restoredDips.Height * dpiY;
        var gripX = Math.Clamp((cursorPixels.X - maximizedPixels.Left) / maximizedPixels.Width, 0, 1);
        var gripY = Math.Clamp((cursorPixels.Y - maximizedPixels.Top) / maximizedPixels.Height, 0, 1);
        return new Rect(cursorPixels.X - gripX * width, cursorPixels.Y - gripY * height, width, height);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.SourceInitialized -= OnSourceInitialized;
        window.Activated -= OnEnvironmentChanged;
        window.StateChanged -= OnEnvironmentChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        source?.RemoveHook(WindowProc);
        if (handle != 0) disable(handle);
        publish(false);
        source = null;
        handle = 0;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref NativeMargins margins);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);
}
