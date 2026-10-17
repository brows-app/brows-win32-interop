using Brows.Win32.InteropServices.ComTypes;
using Brows.Win32.PlatformInvoke;
using System;
using System.Runtime.Versioning;

namespace Brows.Win32.InteropServices;

#if !NETFRAMEWORK
[SupportedOSPlatform("windows")]
#endif
internal sealed class ShellItemWrapper : ComObjectWrapper<IShellItem> {
    protected sealed override IShellItem Factory() {
        var iid = IID.Managed.IShellItem;
        var
        hr = shell32.SHCreateItemFromParsingName(Path, IntPtr.Zero, ref iid, out var shellItem);
        hr.ThrowOnError();
        return shellItem;
    }

    public TResult UseShellItem<TResult>(Func<IShellItem, TResult> function) {
        return UseComObject(function);
    }

    public string Path { get; }

    public ShellItemWrapper(string path) {
        Path = path;
    }
}
