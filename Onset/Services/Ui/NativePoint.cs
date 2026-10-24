using System.Runtime.InteropServices;
namespace Onset.Services.Ui;
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}
