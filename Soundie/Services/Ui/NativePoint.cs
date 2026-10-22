using System.Runtime.InteropServices;
namespace Soundie.Services.Ui;
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}
