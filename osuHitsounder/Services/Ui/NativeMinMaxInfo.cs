using System.Runtime.InteropServices;
namespace osuHitsounder.Services.Ui;
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMinMaxInfo
{
    public NativePoint Reserved;
    public NativePoint MaxSize;
    public NativePoint MaxPosition;
    public NativePoint MinTrackSize;
    public NativePoint MaxTrackSize;
}
