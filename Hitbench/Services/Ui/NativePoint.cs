using System.Runtime.InteropServices;
namespace Hitbench.Services.Ui;
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}
