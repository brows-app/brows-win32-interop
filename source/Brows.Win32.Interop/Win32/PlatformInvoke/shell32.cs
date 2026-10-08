using Brows.Win32.InteropServices.ComTypes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Brows.Win32.PlatformInvoke;

[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "N/A")]
internal static class shell32 {
    public const int MAX_PATH = 260;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true)]
    public static extern bool ShellExecuteExW([In, Out] ref SHELLEXECUTEINFOW lpExecInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern HRESULT SHCreateItemFromParsingName(
        [In] string pszPath,
        [In, Optional] nint pbc,
        [In] ref Guid riid,
        [Out][MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItem ppv);
}
