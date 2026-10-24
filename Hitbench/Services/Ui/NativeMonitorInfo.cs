using System.Runtime.InteropServices;

namespace Hitbench.Services.Ui;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMonitorInfo
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect WorkArea;
    public uint Flags;
}
