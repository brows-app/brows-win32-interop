using System;
using System.Runtime.InteropServices;

namespace Brows.Win32.InteropServices.ComTypes;

[Guid(IID.IShell)]
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShell {
}
