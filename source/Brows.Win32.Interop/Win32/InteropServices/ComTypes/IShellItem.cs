using System.Runtime.InteropServices;

namespace Brows.Win32.InteropServices.ComTypes;

[Guid(IID.IShellItem)]
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem {
}
