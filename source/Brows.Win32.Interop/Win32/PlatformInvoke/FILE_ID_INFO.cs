using System.Runtime.InteropServices;

namespace Brows.Win32.PlatformInvoke;

[StructLayout(LayoutKind.Sequential)]
internal struct FILE_ID_INFO {
    public ulong VolumeSerialNumber;
    public FILE_ID_128 FileId;
}

