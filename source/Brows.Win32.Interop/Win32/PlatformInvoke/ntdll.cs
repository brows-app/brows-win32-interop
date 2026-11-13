using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Brows.Win32.PlatformInvoke;

#if !NETFRAMEWORK
[SupportedOSPlatform("windows")]
#endif
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "N/A")]
#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters
internal static class ntdll {
#pragma warning restore CS8981 // The type name only contains lower-cased ascii characters
    [DllImport("ntdll.dll")]
    [return: MarshalAs(UnmanagedType.U4)]
    public static extern NTSTATUS NtQueryInformationFile(
        [In] SafeHandle FileHandle,
        [In, Out] ref IO_STATUS_BLOCK IoStatusBlock,
        [In, Out] ref FILE_CASE_SENSITIVE_INFORMATION FileInformation,
        [In] uint Length,
        [In] FILE_INFORMATION_CLASS FileInformationClass);
}
