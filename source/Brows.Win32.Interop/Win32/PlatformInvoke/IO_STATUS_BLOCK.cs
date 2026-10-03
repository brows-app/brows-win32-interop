using System.Runtime.InteropServices;

namespace Brows.Win32.PlatformInvoke;

[StructLayout(LayoutKind.Sequential)]
internal struct IO_STATUS_BLOCK {
    public System.IntPtr Status;
    public System.UIntPtr Information;
}
