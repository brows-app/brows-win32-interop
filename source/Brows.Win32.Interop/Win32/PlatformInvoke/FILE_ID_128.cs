using System.Runtime.InteropServices;

namespace Brows.Win32.PlatformInvoke;

[StructLayout(LayoutKind.Sequential)]
internal struct FILE_ID_128 {
    public ulong IdentifierLow;
    public ulong IdentifierHigh;
}
