using System.Runtime.InteropServices;
namespace Hitbench.Services.Ui;
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMargins
{
    public int Left;
    public int Right;
    public int Top;
    public int Bottom;
}
