using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Brows.Win32.PlatformInvoke;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WIN32_FIND_DATAW {
    public uint FileAttributes;
    public FILETIME CreationTime;
    public FILETIME LastAccessTime;
    public FILETIME LastWriteTime;
    public uint FileSizeHigh;
    public uint FileSizeLow;
    public uint Reserved0;
    public uint Reserved1;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string FileName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
    public string AlternateFileName;
}
