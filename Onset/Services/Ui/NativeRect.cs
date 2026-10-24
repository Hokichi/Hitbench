using System.Runtime.InteropServices;

namespace Onset.Services.Ui;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
