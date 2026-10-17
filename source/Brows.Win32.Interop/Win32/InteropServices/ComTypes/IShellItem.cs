using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Brows.Win32.InteropServices.ComTypes;

#if !NETFRAMEWORK
[SupportedOSPlatform("windows")]
#endif
[Guid(IID.IShellItem)]
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem {
}
